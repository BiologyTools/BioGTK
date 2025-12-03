using BioGTK;
using GLib;
using Gtk;
using Mapsui.Widgets;
using Pango;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Application = Gtk.Application;
using Task = System.Threading.Tasks.Task;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System;
using System.Collections.Generic;

namespace BioGTK
{
    public class AI : Window
    {
        private static HttpClient client = new HttpClient();

        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        private string pred = "";
        private List<string> preds = new List<string>();
        public static bool onTab = false;
        public static bool useBioformats = true;
        public static bool headless = false;
        public static bool resultInNewTab = false;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private TextView textBox;
        [Builder.Object]
        private TextView consoleBox;
        [Builder.Object]
        private Button sendBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// Create a new AI object using the Glade file "BioGTK.Glade.AI.glade"
        /// @return A new instance of the AI class.
        public static AI Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/Glade/AI.glade", FileMode.Open));
            return new AI(builder, builder.GetObject("aiConsole").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected AI(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            App.ApplyStyles(this);
            this.ShowAll();
        }

        // -----------------------------------------------------
        // DTOs that match the OpenAI‑compatible payload format
        // -----------------------------------------------------
        public sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

        public sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double? Temperature = null,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens = null);

        public sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage Message,
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("finish_reason")] string FinishReason);

        public sealed record ChatResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("object")] string Object,
        [property: JsonPropertyName("created")] long Created,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);

        public sealed class OllamaChatService
        {
            private readonly HttpClient _httpClient;
            private readonly JsonSerializerOptions _jsonOptions;

            public OllamaChatService(HttpClient httpClient, string baseUrl = "http://localhost:11434")
            {
                _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
                _httpClient.Timeout = TimeSpan.FromMinutes(60);
                if (_httpClient.BaseAddress == null)
                    _httpClient.BaseAddress = new Uri(baseUrl);

                // ✅ Use attributes for naming instead of a broken SnakeCase policy
                _jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null, // respect [JsonPropertyName] attributes
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };
            }

            public async Task<string> GetResponseAsync(
                string systemPrompt,
                string userPrompt,
                CancellationToken cancellationToken = default)
            {
                var request = new ChatRequest(
                    Model: "gpt-oss",
                    Messages: new[]
                    {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
                    });

                var json = JsonSerializer.Serialize(request, _jsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                            cancellationToken, timeoutCts.Token);

                HttpResponseMessage httpResponse;
                try
                {
                    httpResponse = await _httpClient.PostAsync(
                                       "/v1/chat/completions",
                                       content,
                                       linkedCts.Token)
                                     .ConfigureAwait(false);
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new Exception();
                }

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var rawError = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new HttpRequestException(
                        $"Server returned {(int)httpResponse.StatusCode} – {httpResponse.ReasonPhrase}. Body: {rawError}");
                }

                var chatResponse = await httpResponse.Content
                                       .ReadFromJsonAsync<ChatResponse>(_jsonOptions, linkedCts.Token)
                                       .ConfigureAwait(false);

                var firstChoice = chatResponse?.Choices?.Count > 0 ? chatResponse.Choices[0] : null;
                if (firstChoice?.Message?.Content == null)
                    throw new InvalidOperationException("The response does not contain a usable message.");

                return firstChoice.Message.Content;
            }
        }


        // -----------------------------------------------------
        // Your UI‑layer method (the one you originally called)
        // -----------------------------------------------------
        public async Task StartAsync(string args, CancellationToken uiCancel = default)
        {

            try
            {
                var ollama = new OllamaChatService(client);
                string assistantReply = await ollama.GetResponseAsync(
                                            systemPrompt: "You are a C# programmer",
                                            userPrompt: args,
                                            cancellationToken: uiCancel);

                TextIter tr = textBox.Buffer.StartIter;
                textBox.Buffer.Insert(ref tr, assistantReply);
            }
            catch (TimeoutException tex)
            {
                Console.WriteLine($"⌛ Timeout: {tex.Message}");
            }
            catch (HttpRequestException hrex)
            {
                Console.WriteLine($"❌ Server error: {hrex.Message}");
            }
            catch (OperationCanceledException) when (uiCancel.IsCancellationRequested)
            {
                Console.WriteLine("⚪ Request cancelled by the user.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Unexpected error: {ex.Message}");
            }
        }

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            sendBut.ButtonPressEvent += SendBut_ButtonPressEvent;
        }

        private void SendBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            StartAsync(consoleBox.Buffer.Text);
        }
        #endregion
    }
}