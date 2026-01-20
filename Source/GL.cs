using AForge;
using Gdk;
using GLib;
using Gtk;
using ome.xml.model.enums;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using System.IO;
using System.Runtime.InteropServices;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;
using PixelType = OpenTK.Graphics.OpenGL4.PixelType;
namespace BioGTK
{
    public class TileCopyGL : IDisposable
    {
        private int computeShaderProgram;
        private int computeShader;

        // Uniform locations
        private int canvasWidthLocation;
        private int canvasHeightLocation;
        private int tileWidthLocation;
        private int tileHeightLocation;
        private int offsetXLocation;
        private int offsetYLocation;
        private int canvasTileWidthLocation;
        private int canvasTileHeightLocation;

        public TileCopyGL(GLContext gL)
        {
            
            InitializeShaders(gL);
        }

        private void InitializeShaders(GLContext gl)
        {
            var glArea = App.viewer.sk;
            glArea.ShowAll();
            glArea.Show();
            glArea.ShowNow();

            if(glArea.AllocatedWidth == 1 || glArea.AllocatedHeight == 1)
            {
                return;
            }

            // IMPORTANT: Load bindings here
            //Native.LoadBindings(glArea.Context);
            // Create compute shader
            //if (glArea.Error != null)
            //    throw new Exception("OpenGL context creation failed");
            computeShader = GL.CreateShader(ShaderType.ComputeShader);

            // Load shader source
            string shaderSource = LoadShaderSource("tile_copy.comp");
            GL.ShaderSource(computeShader, shaderSource);
            GL.CompileShader(computeShader);

            // Check compilation errors
            GL.GetShader(computeShader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(computeShader);
                throw new Exception($"Compute shader compilation failed: {infoLog}");
            }

            // Create program
            computeShaderProgram = GL.CreateProgram();
            GL.AttachShader(computeShaderProgram, computeShader);
            GL.LinkProgram(computeShaderProgram);

            // Check linking errors
            GL.GetProgram(computeShaderProgram, GetProgramParameterName.LinkStatus, out success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(computeShaderProgram);
                throw new Exception($"Shader program linking failed: {infoLog}");
            }

            // Get uniform locations
            canvasWidthLocation = GL.GetUniformLocation(computeShaderProgram, "canvasWidth");
            canvasHeightLocation = GL.GetUniformLocation(computeShaderProgram, "canvasHeight");
            tileWidthLocation = GL.GetUniformLocation(computeShaderProgram, "tileWidth");
            tileHeightLocation = GL.GetUniformLocation(computeShaderProgram, "tileHeight");
            offsetXLocation = GL.GetUniformLocation(computeShaderProgram, "offsetX");
            offsetYLocation = GL.GetUniformLocation(computeShaderProgram, "offsetY");
            canvasTileWidthLocation = GL.GetUniformLocation(computeShaderProgram, "canvasTileWidth");
            canvasTileHeightLocation = GL.GetUniformLocation(computeShaderProgram, "canvasTileHeight");
        }

        private string LoadShaderSource(string filename)
        {
            // Load from embedded resource or file
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            // Embedded shader source as fallback
            return @"
#version 430

layout(local_size_x = 16, local_size_y = 16) in;

layout(rgba8, binding = 0) uniform readonly image2D tile;
layout(rgba8, binding = 1) uniform writeonly image2D canvas;

uniform int canvasWidth;
uniform int canvasHeight;
uniform int tileWidth;
uniform int tileHeight;
uniform int offsetX;
uniform int offsetY;
uniform int canvasTileWidth;
uniform int canvasTileHeight;

void main()
{
    ivec2 globalID = ivec2(gl_GlobalInvocationID.xy);
    int x = globalID.x;
    int y = globalID.y;

    if (x < canvasTileWidth && y < canvasTileHeight) {
        int canvasX = x + offsetX;
        int canvasY = y + offsetY;

        if (canvasX < canvasWidth && canvasY < canvasHeight) {
            float scaleX = float(tileWidth) / float(canvasTileWidth);
            float scaleY = float(tileHeight) / float(canvasTileHeight);

            int tileX = min(int(float(x) * scaleX), tileWidth - 1);
            int tileY = min(int(float(y) * scaleY), tileHeight - 1);

            vec4 pixel = imageLoad(tile, ivec2(tileX, tileY));
            imageStore(canvas, ivec2(canvasX, canvasY), pixel);
        }
    }
}";
        }

        /// <summary>
        /// Copy a tile to canvas using OpenGL compute shader
        /// </summary>
        public void CopyTileToCanvas(
            int canvasTexture,
            int canvasWidth,
            int canvasHeight,
            int tileTexture,
            int tileWidth,
            int tileHeight,
            int offsetX,
            int offsetY,
            int canvasTileWidth,
            int canvasTileHeight)
        {
            // Use the compute shader program
            GL.UseProgram(computeShaderProgram);

            // Set uniforms
            GL.Uniform1(canvasWidthLocation, canvasWidth);
            GL.Uniform1(canvasHeightLocation, canvasHeight);
            GL.Uniform1(tileWidthLocation, tileWidth);
            GL.Uniform1(tileHeightLocation, tileHeight);
            GL.Uniform1(offsetXLocation, offsetX);
            GL.Uniform1(offsetYLocation, offsetY);
            GL.Uniform1(canvasTileWidthLocation, canvasTileWidth);
            GL.Uniform1(canvasTileHeightLocation, canvasTileHeight);

            // Bind textures as images
            GL.BindImageTexture(0, tileTexture, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
            GL.BindImageTexture(1, canvasTexture, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

            // Calculate work group counts (round up division)
            int workGroupsX = (canvasTileWidth + 15) / 16;
            int workGroupsY = (canvasTileHeight + 15) / 16;

            // Dispatch compute shader
            GL.DispatchCompute(workGroupsX, workGroupsY, 1);

            // Ensure compute shader finishes before reading
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        /// <summary>
        /// Copy tile from byte array to canvas texture
        /// </summary>
        public void CopyTileToCanvas(
            int canvasTexture,
            int canvasWidth,
            int canvasHeight,
            byte[] tileData,
            int tileWidth,
            int tileHeight,
            int offsetX,
            int offsetY,
            int canvasTileWidth,
            int canvasTileHeight)
        {
            // Create temporary texture for tile data
            int tileTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tileTexture);

            // Upload tile data
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgb,
                tileWidth,
                tileHeight,
                0,
                PixelFormat.Rgb,
                PixelType.UnsignedByte,
                tileData);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Copy tile to canvas
            CopyTileToCanvas(
                canvasTexture,
                canvasWidth,
                canvasHeight,
                tileTexture,
                tileWidth,
                tileHeight,
                offsetX,
                offsetY,
                canvasTileWidth,
                canvasTileHeight);

            // Cleanup temporary texture
            GL.DeleteTexture(tileTexture);
        }

        /// <summary>
        /// Create a canvas texture
        /// </summary>
        public int CreateCanvasTexture(int width, int height)
        {
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba8,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                IntPtr.Zero);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            return texture;
        }

        /// <summary>
        /// Read canvas texture back to CPU memory
        /// </summary>
        public byte[] ReadCanvasTexture(int canvasTexture, int width, int height)
        {
            byte[] data = new byte[width * height * 4]; // RGBA

            GL.BindTexture(TextureTarget.Texture2D, canvasTexture);
            GL.GetTexImage(
                TextureTarget.Texture2D,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                data);

            return data;
        }

        public class GlWidget : GLArea
        {
            private bool initialized;

            public GlWidget()
            {
                HasDepthBuffer = true;
                AutoRender = true;

                Realized += OnRealized;
                Render += GlWidget_Render;
            }

            private void GlWidget_Render(object o, RenderArgs args)
            {
                throw new NotImplementedException();
            }

            private void OnRealized(object sender, EventArgs e)
            {
                MakeCurrent();

                if (Error != null)
                    throw new Exception("Failed to create OpenGL context");
                /*
                if (!initialized)
                {
                    LoadOpenTkBindings();
                    initialized = true;
                }
                */
            }

            private bool OnRender(object sender, RenderArgs args)
            {
                MakeCurrent();
                GL.Viewport(0, 0, AllocatedWidth, AllocatedHeight);
                GL.ClearColor(0f, 0f, 0f, 1f);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // Your GL rendering here

                return true;
            }
        }

        public void Dispose()
        {
            if (computeShader != 0)
            {
                GL.DeleteShader(computeShader);
                computeShader = 0;
            }

            if (computeShaderProgram != 0)
            {
                GL.DeleteProgram(computeShaderProgram);
                computeShaderProgram = 0;
            }
        }
    }
}