using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BruTile;
using Gtk;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using AForge;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;

namespace BioGTK
{
    /// <summary>
    /// A GTK GLArea widget that renders pyramidal slide tiles directly on the GPU.
    /// Eliminates ReadPixels overhead by rendering to screen instead of copying to CPU.
    /// Supports SkiaSharp overlay for annotations via GRContext.
    /// </summary>
    public class SlideGLArea : GLArea
    {
        private const bool VerboseLogging = false;
        private static readonly bool DiagnosticLogging = false;
        // ============================================================================
        // GL Resources
        // ============================================================================

        private bool _glInitialized;
        private int _vao;
        private int _vbo;
        private int _shaderProgram;
        private int _pyramidPreviewTexture;
        private SKImage _uploadedPyramidPreview;
        private int _completeFrameTexture;
        private int _completeFrameWidth;
        private int _completeFrameHeight;
        private int _completeFrameVersion = -1;
        private (float X, float Y, float Width, float Height) _completeFrameImageBounds;
        private (int Width, int Height) _completeFrameViewport;
        private bool _tileFrameComplete;

        // Uniform locations (cached for performance)
        private int _locPos;
        private int _locSize;
        private int _locViewportSize;
        private int _locTex;
        private int _locUvMin;
        private int _locUvMax;

        // ============================================================================
        // Texture Cache - tiles uploaded to GPU
        // ============================================================================

        private Dictionary<TileIndex, int> _textureCache = new();
        private Dictionary<TileIndex, (int w, int h)> _textureSizes = new();
        private readonly HashSet<TileIndex> _protectedTileIndices = new();
        private long _textureBytes;
        private const int MAX_CACHED_TEXTURES = 128;
        private const long MAX_CACHED_TEXTURE_BYTES = 96L * 1024 * 1024;

        // ============================================================================
        // Skia GL Backend for annotations
        // ============================================================================

        private GRContext _grContext;
        private GRBackendRenderTarget _renderTarget;
        private SKSurface _skSurface;

        // ============================================================================
        // Rendering State
        // ============================================================================

        public List<TileRenderInfo> TilesToRender { get; } = new();
        public bool NeedsRedraw { get; set; } = true;
        public bool PreservePreviousFrameWhileIncomplete { get; set; } = false;
        public int TileFrameVersion { get; set; }
        internal Func<SKImage> GetPyramidPreview { get; set; }
        private Action<byte[], int, int> _pendingWholeImageCapture;

        // Screen-space boundary of the image (in pixels from top-left).
        // Tiles are scissored to this rect so nothing renders outside the image.
        // Set by SlideRenderer before each draw call. (-1 means no clipping.)
        public float ImageScreenX { get; set; } = -1;
        public float ImageScreenY { get; set; } = -1;
        public float ImageScreenW { get; set; } = -1;
        public float ImageScreenH { get; set; } = -1;

        /// <summary>
        /// Event fired during Skia rendering phase for annotation drawing.
        /// </summary>
        public event Action<SKCanvas, int, int> OnSkiaRender;

        // ============================================================================
        // Shaders
        // ============================================================================

        private const string VertexShaderSource = @"
#version 330 core

layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;

uniform vec2 pos;           // pixel-space top-left of tile
uniform vec2 size;          // pixel-space size of tile
uniform vec2 viewportSize;  // (width, height) in pixels
uniform vec2 uvMin;         // UV sub-region min (for boundary clipping)
uniform vec2 uvMax;         // UV sub-region max (for boundary clipping)

out vec2 uv;

void main()
{
    // aPos is in [0,1] x [0,1]
    vec2 pixelPos = aPos * size + pos;

    // Convert from pixel coords to NDC (-1..1), with Y flipped for GL
    vec2 ndc;
    ndc.x = (pixelPos.x / viewportSize.x) * 2.0 - 1.0;
    ndc.y = 1.0 - (pixelPos.y / viewportSize.y) * 2.0;

    gl_Position = vec4(ndc, 0.0, 1.0);

    // Map aUV [0,1] to the sub-region [uvMin, uvMax]
    uv = uvMin + aUV * (uvMax - uvMin);
}
";

        private const string FragmentShaderSource = @"
#version 330 core

in vec2 uv;
out vec4 FragColor;
uniform sampler2D tex;

void main()
{
    // Fix: Removed '1.0 - uv.y'. 
    // Image data is usually Top-Down. OpenGL Textures are Bottom-Up.
    // This implicit flip means UV(0,0) (Top-Left Quad) maps to Texture Bottom (Image Top).
    // This is the correct orientation for Top-Left origin rendering.
    FragColor = texture(tex, vec2(uv.x, uv.y));
}
";

        // ============================================================================
        // Construction
        // ============================================================================

        public SlideGLArea()
        {
            HasDepthBuffer = false;
            HasStencilBuffer = false;
            // Skia wraps this framebuffer as GL_RGBA8, so GTK must allocate an
            // alpha-capable color attachment too. The final render pass explicitly
            // writes alpha=1 across the surface, keeping presentation opaque while
            // avoiding the undefined RGB-buffer-as-RGBA mismatch.
            HasAlpha = true;
            AutoRender = false;  // We control when to render

            Realized += OnRealized;
            //Unrealize += OnUnrealize;
            Render += OnRender;
            Resize += OnResized;
        }
        // ============================================================================
        // GLArea Lifecycle
        // ============================================================================

        private void OnRealized(object sender, EventArgs e)
        {
            MakeCurrent();

            if (Error != 0)
            {
                if (VerboseLogging) Console.WriteLine($"GLArea error on realize.");
                return;
            }

            InitializeGL();
            InitializeSkia();

            _pyramidPreviewTexture = 0;
            _uploadedPyramidPreview = null;
            _glInitialized = true;
            Gtk.Application.Invoke((s, a) => App.viewer?.RequestDeferredRender());
        }

        private void OnResized(object sender, EventArgs e)
        {
            if (!_glInitialized) return;

            MakeCurrent();

            // Recreate Skia surface for new size
            CleanupSkia();
            InitializeSkia();

            NeedsRedraw = true;
            Gtk.Application.Invoke((s, a) => App.viewer?.RequestDeferredRender());
        }

        // ============================================================================
        // GL Initialization
        // ============================================================================

        private void InitializeGL()
        {
            // Compile shaders
            int vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
            int fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vertexShader);
            GL.AttachShader(_shaderProgram, fragmentShader);
            GL.LinkProgram(_shaderProgram);

            GL.GetProgram(_shaderProgram, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
            {
                string infoLog = GL.GetProgramInfoLog(_shaderProgram);
                throw new Exception($"Shader link failed: {infoLog}");
            }

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // Cache uniform locations
            _locPos = GL.GetUniformLocation(_shaderProgram, "pos");
            _locSize = GL.GetUniformLocation(_shaderProgram, "size");
            _locViewportSize = GL.GetUniformLocation(_shaderProgram, "viewportSize");
            _locTex = GL.GetUniformLocation(_shaderProgram, "tex");
            _locUvMin = GL.GetUniformLocation(_shaderProgram, "uvMin");
            _locUvMax = GL.GetUniformLocation(_shaderProgram, "uvMax");

            // Create quad VAO/VBO for tile rendering
            float[] quadVertices =
            {
                // pos    uv
                0, 0,     0, 0,
                1, 0,     1, 0,
                1, 1,     1, 1,

                0, 0,     0, 0,
                1, 1,     1, 1,
                0, 1,     0, 1
            };

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float),
                          quadVertices, BufferUsageHint.StaticDraw);

            // Position attribute
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // UV attribute
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation failed ({type}): {infoLog}");
            }

            return shader;
        }

        private void CleanupGL()
        {
            if (_shaderProgram != 0)
            {
                GL.DeleteProgram(_shaderProgram);
                _shaderProgram = 0;
            }

            if (_vbo != 0)
            {
                GL.DeleteBuffer(_vbo);
                _vbo = 0;
            }

            if (_vao != 0)
            {
                GL.DeleteVertexArray(_vao);
                _vao = 0;
            }

            ClearTextureCache();
            DiscardCompleteFrame();
            DiscardPyramidPreview();
        }

        // ============================================================================
        // Skia Initialization
        // ============================================================================

        private void InitializeSkia(int framebuffer = -1)
        {
            // Capture GTK's framebuffer before disposing the previous Skia target;
            // target disposal is allowed to change GL bindings.
            if (framebuffer < 0)
                GL.GetInteger(GetPName.FramebufferBinding, out framebuffer);

            // Keep one GPU context for the lifetime of the GLArea. Creating a new
            // GRContext on every frame leaks native GL resources and eventually
            // causes allocations such as GL.GenTexture to return 0.
            _grContext ??= GRContext.CreateGl();

            // GTK may rotate the GLArea framebuffer between renders. Recreate only
            // the lightweight target/surface wrappers for the currently bound FBO.
            _skSurface?.Dispose();
            _skSurface = null;
            _renderTarget?.Dispose();
            _renderTarget = null;

            int targetWidth = AllocatedWidth * ScaleFactor;
            int targetHeight = AllocatedHeight * ScaleFactor;
            if (_grContext == null || targetWidth <= 0 || targetHeight <= 0)
                return;

            // Ensure the format matches what Gtk.GLArea provides (usually GL_RGBA8)
            var framebufferInfo = new GRGlFramebufferInfo((uint)framebuffer, 0x8058); // GL_RGBA8

            _renderTarget = new GRBackendRenderTarget(
                targetWidth,
                targetHeight,
                0, 8, framebufferInfo);

            _skSurface = SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        }

        private void CleanupSkia()
        {
            _skSurface?.Dispose();
            _skSurface = null;

            _renderTarget?.Dispose();
            _renderTarget = null;

            _grContext?.Dispose();
            _grContext = null;
        }

        // ============================================================================
        // Rendering
        // ============================================================================

        private void OnRender(object sender, RenderArgs args)
        {
            if (!_glInitialized)
            {
                args.RetVal = true;
                return;
            }

            MakeCurrent();
            GL.GetInteger(GetPName.FramebufferBinding, out int presentationFramebuffer);

            int width = AllocatedWidth;
            int height = AllocatedHeight;

            if (width <= 0 || height <= 0)
            {
                args.RetVal = true;
                return;
            }

            // Phase 1: Render tiles with OpenGL
            RenderTiles(width, height);
            if (_tileFrameComplete)
                CaptureCompleteTileFrame(width, height);

            // The initial fit-to-window frame already contains everything needed
            // by the minimap. Capture it before Skia adds the overview and ROIs so
            // ImageView does not perform a second remote pyramid read.
            CaptureWholeImageFrameIfRequested(width, height);

            // Phase 2: Render annotations with Skia
            try
            {
                RenderSkiaOverlay(width, height, presentationFramebuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skia overlay render error: {ex.Message}");
            }

            // Skia surfaces are alpha-capable even though the viewer window is not.
            // Some Windows GL/GDK combinations leave untouched overlay pixels with
            // alpha zero. Rewrite only the alpha channel after the overlay so the
            // compositor can never expose windows behind the viewer.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, presentationFramebuffer);
            ForceOpaqueFramebuffer();
            GL.Flush();
            args.RetVal = true;
        }

        private static void ForceOpaqueFramebuffer()
        {
            GL.Disable(EnableCap.ScissorTest);
            GL.ColorMask(false, false, false, true);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.ColorMask(true, true, true, true);
        }

        private void RenderTiles(int width, int height)
        {
            _tileFrameComplete = false;
            int scale = ScaleFactor;
            GL.Viewport(0, 0, width * scale, height * scale);

            // GLArea may provide a different framebuffer for each render. Its
            // previous contents are undefined, so returning early in an attempt
            // to preserve the prior frame can present transparent pixels to the
            // window compositor. Establish a fully opaque frame on every pass;
            // cached fallback tiles are drawn over it below when available.
            GL.Disable(EnableCap.ScissorTest);
            GL.ColorMask(true, true, true, true);
            GL.ClearColor(0.2f, 0.2f, 0.2f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            if (TilesToRender.Count == 0)
            {
                if (PreservePreviousFrameWhileIncomplete)
                    RenderCompleteFrameFallback(width, height);
                LogDiag($"[RenderTiles] no tiles to render viewport={width}x{height} imageScreen=({ImageScreenX},{ImageScreenY},{ImageScreenW},{ImageScreenH})");
                return;
            }

            bool incomplete = PreservePreviousFrameWhileIncomplete ||
                TilesToRender.Any(tile => !HasTileTexture(tile.Index));
            if (incomplete)
            {
                bool hasPreview = RenderPyramidPreview(width, height);
                if (_completeFrameVersion == TileFrameVersion ||
                    (!hasPreview && !TilesToRender.Any(tile => HasTileTexture(tile.Index))))
                {
                    RenderCompleteFrameFallback(width, height);
                }
                else
                {
                    // Replace the older plane as soon as pixels for the new plane
                    // are available, whether from its preview or its first tile.
                    DiscardCompleteFrame();
                }
            }

            LogDiag($"[RenderTiles] tiles={TilesToRender.Count} viewport={width}x{height} imageScreen=({ImageScreenX},{ImageScreenY},{ImageScreenW},{ImageScreenH})");

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Scissor to the image boundary so tiles cannot render pixels outside the image.
            // GL scissor Y is from bottom; convert from top-left screen coords.
            if (ImageScreenW > 0 && ImageScreenH > 0)
            {
                GL.Enable(EnableCap.ScissorTest);
                // Apply (int) cast AFTER multiplying by the HiDPI scale factor so
                // sub-pixel rounding errors don't compound.
                int sx = (int)(Math.Max(0, ImageScreenX) * scale);
                int sy = (int)(Math.Max(0, (height - ImageScreenY - ImageScreenH)) * scale);
                int sw = (int)(Math.Min(ImageScreenW, width  - ImageScreenX) * scale);
                int sh = (int)(Math.Min(ImageScreenH, height - ImageScreenY)  * scale);
                GL.Scissor(sx, sy, Math.Max(0, sw), Math.Max(0, sh));
            }

            GL.UseProgram(_shaderProgram);
            GL.BindVertexArray(_vao);

            GL.Uniform2(_locViewportSize, (float)width, (float)height);
            GL.Uniform1(_locTex, 0);
            GL.ActiveTexture(TextureUnit.Texture0);

            int renderedCount = 0;
            foreach (var tile in TilesToRender)
            {
                if (!_textureCache.TryGetValue(tile.Index, out int texId))
                {
                    LogDiag($"[RenderTiles] missing texture tile={tile.Index}");
                    continue;
                }

                GL.Uniform2(_locPos, tile.ScreenX, tile.ScreenY);
                GL.Uniform2(_locSize, tile.ScreenWidth, tile.ScreenHeight);
                GL.Uniform2(_locUvMin, tile.U0, tile.V0);
                GL.Uniform2(_locUvMax, tile.U1, tile.V1);

                LogDiag($"[RenderTiles] draw tile={tile.Index} pos=({tile.ScreenX:F1},{tile.ScreenY:F1}) size=({tile.ScreenWidth:F1},{tile.ScreenHeight:F1}) tex={texId}");

                GL.BindTexture(TextureTarget.Texture2D, texId);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                renderedCount++;
            }

            LogDiag($"[RenderTiles] rendered={renderedCount}");

            GL.Disable(EnableCap.ScissorTest);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.Disable(EnableCap.Blend);
            _tileFrameComplete = !incomplete;
        }

        private bool RenderPyramidPreview(int width, int height)
        {
            var preview = GetPyramidPreview?.Invoke();
            if (preview == null || ImageScreenW <= 0 || ImageScreenH <= 0)
                return false;

            if (!ReferenceEquals(preview, _uploadedPyramidPreview))
            {
                DiscardPyramidPreview();
                using var pixels = new SKBitmap(preview.Width, preview.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (!preview.ReadPixels(pixels.PeekPixels(), 0, 0))
                    return false;
                _pyramidPreviewTexture = UploadBgraTexture(pixels.GetPixels(), pixels.Width, pixels.Height);
                if (_pyramidPreviewTexture == 0)
                    return false;
                _uploadedPyramidPreview = preview;
            }

            DrawFrameTexture(_pyramidPreviewTexture, width, height,
                SKRect.Create(ImageScreenX, ImageScreenY, ImageScreenW, ImageScreenH), flipVertically: false);
            return true;
        }

        internal void DiscardPyramidPreview()
        {
            if (_pyramidPreviewTexture != 0 && _glInitialized)
            {
                MakeCurrent();
                GL.DeleteTexture(_pyramidPreviewTexture);
            }
            _pyramidPreviewTexture = 0;
            _uploadedPyramidPreview = null;
        }

        private void RenderCompleteFrameFallback(int width, int height)
        {
            if (_completeFrameTexture == 0 || _completeFrameWidth <= 0 || _completeFrameHeight <= 0)
                return;

            var bounds = SKRect.Create(width, height);
            var previousBounds = _completeFrameImageBounds;
            if (_completeFrameVersion == TileFrameVersion && previousBounds.Width > 0 &&
                previousBounds.Height > 0 && ImageScreenW > 0 && ImageScreenH > 0)
            {
                float scaleX = ImageScreenW / previousBounds.Width;
                float scaleY = ImageScreenH / previousBounds.Height;
                bounds = SKRect.Create(ImageScreenX - previousBounds.X * scaleX,
                    ImageScreenY - previousBounds.Y * scaleY,
                    _completeFrameViewport.Width * scaleX, _completeFrameViewport.Height * scaleY);
            }
            // Framebuffer copies use bottom-left texture origin, unlike uploaded
            // top-down tile bytes, so reverse V while drawing the snapshot.
            DrawFrameTexture(_completeFrameTexture, width, height, bounds, flipVertically: true);
        }

        private void DrawFrameTexture(int texture, int width, int height, SKRect bounds, bool flipVertically)
        {
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);
            GL.UseProgram(_shaderProgram);
            GL.BindVertexArray(_vao);
            GL.Uniform2(_locViewportSize, (float)width, (float)height);
            GL.Uniform1(_locTex, 0);
            GL.Uniform2(_locPos, bounds.Left, bounds.Top);
            GL.Uniform2(_locSize, bounds.Width, bounds.Height);
            GL.Uniform2(_locUvMin, 0f, flipVertically ? 1f : 0f);
            GL.Uniform2(_locUvMax, 1f, flipVertically ? 0f : 1f);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        private void CaptureCompleteTileFrame(int width, int height)
        {
            int physicalWidth = width * Math.Max(1, ScaleFactor);
            int physicalHeight = height * Math.Max(1, ScaleFactor);
            if (physicalWidth <= 0 || physicalHeight <= 0)
                return;

            if (_completeFrameTexture == 0)
                _completeFrameTexture = GL.GenTexture();
            if (_completeFrameTexture == 0)
                return;

            GL.BindTexture(TextureTarget.Texture2D, _completeFrameTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            if (_completeFrameWidth != physicalWidth || _completeFrameHeight != physicalHeight)
            {
                GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    0, 0, physicalWidth, physicalHeight, 0);
                _completeFrameWidth = physicalWidth;
                _completeFrameHeight = physicalHeight;
            }
            else
            {
                GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                    0, 0, physicalWidth, physicalHeight);
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _completeFrameVersion = TileFrameVersion;
            _completeFrameImageBounds = (ImageScreenX, ImageScreenY, ImageScreenW, ImageScreenH);
            _completeFrameViewport = (width, height);
        }

        public void DiscardCompleteFrame()
        {
            if (_completeFrameTexture != 0 && _glInitialized)
            {
                MakeCurrent();
                GL.DeleteTexture(_completeFrameTexture);
            }
            _completeFrameTexture = 0;
            _completeFrameWidth = 0;
            _completeFrameHeight = 0;
        }

        private void CaptureWholeImageFrameIfRequested(int width, int height)
        {
            var callback = _pendingWholeImageCapture;
            if (callback == null || !_tileFrameComplete)
                return;

            // Only use a frame in which the complete image fits in the viewport.
            // A later zoomed/panned frame is not a valid overview source.
            const float tolerance = 1.5f;
            if (ImageScreenW <= 0 || ImageScreenH <= 0 ||
                ImageScreenX < -tolerance || ImageScreenY < -tolerance ||
                ImageScreenX + ImageScreenW > width + tolerance ||
                ImageScreenY + ImageScreenH > height + tolerance)
                return;

            int scale = Math.Max(1, ScaleFactor);
            int framebufferWidth = width * scale;
            int framebufferHeight = height * scale;
            int left = Math.Clamp((int)Math.Round(ImageScreenX * scale), 0, framebufferWidth - 1);
            int top = Math.Clamp((int)Math.Round(ImageScreenY * scale), 0, framebufferHeight - 1);
            int captureWidth = Math.Clamp((int)Math.Round(ImageScreenW * scale), 1, framebufferWidth - left);
            int captureHeight = Math.Clamp((int)Math.Round(ImageScreenH * scale), 1, framebufferHeight - top);
            int glBottom = framebufferHeight - top - captureHeight;

            byte[] rgbaBottomUp = new byte[captureWidth * captureHeight * 4];
            GL.ReadPixels(left, glBottom, captureWidth, captureHeight,
                PixelFormat.Rgba, PixelType.UnsignedByte, rgbaBottomUp);

            // AForge's 32-bit bitmap path expects BGRA rows from top to bottom.
            byte[] bgraTopDown = new byte[rgbaBottomUp.Length];
            int rowBytes = captureWidth * 4;
            for (int y = 0; y < captureHeight; y++)
            {
                int sourceRow = (captureHeight - 1 - y) * rowBytes;
                int destinationRow = y * rowBytes;
                for (int x = 0; x < captureWidth; x++)
                {
                    int source = sourceRow + x * 4;
                    int destination = destinationRow + x * 4;
                    bgraTopDown[destination] = rgbaBottomUp[source + 2];
                    bgraTopDown[destination + 1] = rgbaBottomUp[source + 1];
                    bgraTopDown[destination + 2] = rgbaBottomUp[source];
                    bgraTopDown[destination + 3] = 255;
                }
            }

            _pendingWholeImageCapture = null;
            callback(bgraTopDown, captureWidth, captureHeight);
        }

        private void RenderSkiaOverlay(int width, int height, int presentationFramebuffer)
        {
            if (OnSkiaRender == null) return;
            InitializeSkia(presentationFramebuffer);
            if (_skSurface == null)
                return;

            // Tile rendering uses raw OpenGL immediately before Skia. Tell Skia
            // that every cached GL binding/state may have changed so it rebinds
            // the current GTK framebuffer, program, viewport and blend state.
            _grContext.ResetContext(GRGlBackendState.All);
            var canvas = _skSurface.Canvas;
            // Fire event for annotation drawing
            OnSkiaRender?.Invoke(canvas, width, height);
            _grContext.Flush();
            _grContext.Submit(true);
        }

        // ============================================================================
        // Texture Management
        // ============================================================================

        /// <summary>
        /// Upload a tile texture to the GPU. Call from the main thread.
        /// </summary>
        public void UploadTileTexture(TileIndex index, byte[] pixelData, int tileWidth, int tileHeight)
        {
            if (!_glInitialized)
            {
                LogDiag($"[UploadTileTexture] gl-not-initialized tile={index}");
                return;
            }

            if (pixelData == null || pixelData.Length == 0)
            {
                LogDiag($"[UploadTileTexture] empty pixel data tile={index}");
                return;
            }

            // The buffer is always BGRA = 4 bytes per pixel.
            // GetTile pads every tile — including edge tiles — to exactly
            // tileWidth x tileHeight pixels, so the dimensions passed by SlideRenderer
            // (the schema tile size) always match the buffer. We just validate and bail
            // on any genuine mismatch rather than trying to guess new dimensions.
            if (tileWidth <= 0 || tileHeight <= 0)
            {
                LogDiag($"[UploadTileTexture] invalid dimensions tile={index} size={tileWidth}x{tileHeight}");
                return;
            }

            int expectedBytes = tileWidth * tileHeight * 4;
            if (pixelData.Length < expectedBytes)
            {
                LogDiag($"[UploadTileTexture] buffer too small tile={index} need={expectedBytes} got={pixelData.Length}");
                return;
            }

            MakeCurrent();

            // Check if already cached
            if (_textureCache.ContainsKey(index))
            {
                LogDiag($"[UploadTileTexture] already cached tile={index}");
                return;
            }

            long newTextureBytes = (long)tileWidth * tileHeight * 4;

            // Evict old textures if the cache is full or if the new upload would
            // push the total texture budget over its byte cap.
            while (_textureCache.Count > 0 &&
                   (_textureCache.Count >= MAX_CACHED_TEXTURES ||
                    _textureBytes + newTextureBytes > MAX_CACHED_TEXTURE_BYTES))
            {
                EvictOldestTextures(1);
            }

            // Pin the managed array explicitly so the GC cannot relocate it while the
            // driver is reading from the pointer.
            int tex;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelData, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                tex = UploadBgraTexture(handle.AddrOfPinnedObject(), tileWidth, tileHeight);
            }
            finally
            {
                handle.Free();
            }
            if (tex == 0)
            {
                Console.WriteLine($"OpenGL failed to allocate texture for tile {index}.");
                return;
            }

            _textureCache[index] = tex;
            _textureSizes[index] = (tileWidth, tileHeight);
            _textureBytes += newTextureBytes;
            LogDiag($"[UploadTileTexture] uploaded tile={index} tex={tex} size={tileWidth}x{tileHeight} bytes={pixelData.Length}");
        }

        private static int UploadBgraTexture(IntPtr pixels, int width, int height)
        {
            int texture = GL.GenTexture();
            if (texture == 0)
                return 0;

            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return texture;
        }

        /// <summary>
        /// Get the uploaded texture dimensions for a cached tile.
        /// Returns (0,0) if the tile is not in the cache.
        /// </summary>
        public void GetUploadedTileSize(TileIndex index, out int w, out int h)
        {
            if (_textureSizes.TryGetValue(index, out var sz))
            { w = sz.w; h = sz.h; }
            else
            { w = 0; h = 0; }
        }

        /// <summary>
        /// Check if a tile texture is already in the GPU cache.
        /// </summary>
        public bool HasTileTexture(TileIndex index)
        {
            return _textureCache.ContainsKey(index);
        }

        /// <summary>
        /// Release a specific tile texture.
        /// </summary>
        public void ReleaseTileTexture(TileIndex index)
        {
            if (_textureCache.TryGetValue(index, out int tex))
            {
                MakeCurrent();
                GL.DeleteTexture(tex);
                _textureCache.Remove(index);
                if (_textureSizes.TryGetValue(index, out var sz))
                {
                    _textureBytes -= (long)sz.w * sz.h * 4;
                    _textureSizes.Remove(index);
                }
            }
        }

        /// <summary>
        /// Release all textures for a specific pyramid level.
        /// </summary>
        public void ReleaseLevelTextures(int level)
        {
            if (!_glInitialized) return;

            MakeCurrent();

            var toRemove = _textureCache.Where(kvp => kvp.Key.Level == level).ToList();
            foreach (var kvp in toRemove)
            {
                GL.DeleteTexture(kvp.Value);
                if (_textureSizes.TryGetValue(kvp.Key, out var sz))
                {
                    _textureBytes -= (long)sz.w * sz.h * 4;
                    _textureSizes.Remove(kvp.Key);
                }
                _textureCache.Remove(kvp.Key);
            }
        }

        /// <summary>
        /// Clear all cached textures.
        /// </summary>
        public void ClearTextureCache()
        {
            if (!_glInitialized) return;

            MakeCurrent();

            foreach (var tex in _textureCache.Values)
            {
                GL.DeleteTexture(tex);
            }
            _textureCache.Clear();
            _textureSizes.Clear();
            _protectedTileIndices.Clear();
            _textureBytes = 0;
        }

        private void EvictOldestTextures(int count)
        {
            // Prefer off-screen textures. Tiles in the displayed viewport and
            // incoming tiles for a pending level switch must remain resident long
            // enough to form a complete frame.
            var toRemove = _textureCache
                .Where(kvp => !_protectedTileIndices.Contains(kvp.Key))
                .Take(count)
                .ToList();

            // This should only be needed if a viewport itself exceeds the cache.
            if (toRemove.Count < count)
            {
                var selected = new HashSet<TileIndex>(toRemove.Select(kvp => kvp.Key));
                toRemove.AddRange(_textureCache
                    .Where(kvp => !selected.Contains(kvp.Key))
                    .Take(count - toRemove.Count));
            }
            foreach (var kvp in toRemove)
            {
                GL.DeleteTexture(kvp.Value);
                if (_textureSizes.TryGetValue(kvp.Key, out var sz))
                {
                    _textureBytes -= (long)sz.w * sz.h * 4;
                    _textureSizes.Remove(kvp.Key);
                }
                _textureCache.Remove(kvp.Key);
            }
        }

        // ============================================================================
        // Public API
        // ============================================================================

        /// <summary>
        /// Request a redraw of the GLArea.
        /// </summary>
        public void RequestRedraw()
        {
            NeedsRedraw = true;
            QueueRender();
            QueueDraw();
        }

        /// <summary>
        /// Captures the next complete fit-to-window image frame. A newer request
        /// replaces an older one because only the current image/coordinate matters.
        /// </summary>
        public void CaptureNextWholeImageFrame(Action<byte[], int, int> callback)
        {
            _pendingWholeImageCapture = callback;
            RequestRedraw();
        }

        public void CancelWholeImageFrameCapture(Action<byte[], int, int> callback)
        {
            if (ReferenceEquals(_pendingWholeImageCapture, callback))
                _pendingWholeImageCapture = null;
        }

        /// <summary>
        /// Prepare tiles for rendering. Call before RequestRedraw().
        /// </summary>
        public void SetTilesToRender(IEnumerable<TileRenderInfo> tiles)
        {
            TilesToRender.Clear();
            TilesToRender.AddRange(tiles);
            LogDiag($"[SetTilesToRender] count={TilesToRender.Count}");
        }

        /// <summary>
        /// Protects incoming visible tiles from FIFO eviction. The currently
        /// displayed set is included automatically so progressive level changes
        /// retain both the fallback frame and the target frame under construction.
        /// </summary>
        public void SetProtectedTileIndices(IEnumerable<TileIndex> indices)
        {
            _protectedTileIndices.Clear();
            foreach (var index in indices)
                _protectedTileIndices.Add(index);
            foreach (var tile in TilesToRender)
                _protectedTileIndices.Add(tile.Index);
        }

        public int CachedTextureCount => _textureCache.Count;

        /// <summary>
        /// Read pixels from the current framebuffer (for export/save operations).
        /// This is slow - only use for export, not display.
        /// </summary>
        public byte[] ReadPixels()
        {
            if (!_glInitialized) return null;

            MakeCurrent();

            int width = AllocatedWidth;
            int height = AllocatedHeight;

            byte[] pixels = new byte[width * height * 4];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            return pixels;
        }

        private static void LogDiag(string message)
        {
            if (!DiagnosticLogging)
                return;

            AppLog.Append(message);
        }
    }

    // ============================================================================
    // Supporting Types
    // ============================================================================

    /// <summary>
    /// Information needed to render a single tile.
    /// </summary>
    public struct TileRenderInfo
    {
        public TileIndex Index;

        // Screen-space position and size in pixels
        public float ScreenX;
        public float ScreenY;
        public float ScreenWidth;
        public float ScreenHeight;

        // UV sub-region within the tile texture (for boundary clipping)
        public float U0, V0, U1, V1;

        public TileRenderInfo(TileIndex index, float x, float y, float w, float h)
        {
            Index = index;
            ScreenX = x;
            ScreenY = y;
            ScreenWidth = w;
            ScreenHeight = h;
            U0 = 0f; V0 = 0f; U1 = 1f; V1 = 1f;
        }

        public TileRenderInfo(TileIndex index, float x, float y, float w, float h,
                              float u0, float v0, float u1, float v1)
        {
            Index = index;
            ScreenX = x;
            ScreenY = y;
            ScreenWidth = w;
            ScreenHeight = h;
            U0 = u0; V0 = v0; U1 = u1; V1 = v1;
        }
    }
}
