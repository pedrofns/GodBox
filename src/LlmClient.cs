using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldfallAIChat
{
    public class ChatMessage
    {
        public string Role;    // "system" | "user" | "assistant"
        public string Content;
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>
    /// Minimal OpenAI-compatible chat-completions client.
    /// Runs the HTTP call on a thread-pool thread (never blocks Unity's main thread) and
    /// delivers the result back on the main thread through Main.RunOnMainThread.
    /// </summary>
    public static class LlmClient
    {
        static LlmClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static void Complete(DialogueConfig cfg, string model, List<ChatMessage> messages, int maxTokens,
            Action<string> onOk, Action<string> onError)
            => Complete(cfg, model, messages, maxTokens, onOk, onError, false, float.NaN);

        /// <param name="jsonMode">v6: ask for a JSON object (response_format json_object; Ollama/OpenAI support it).</param>
        /// <param name="temperatureOverride">v6: NaN = use config; otherwise send this temperature (e.g. 0 for the classifier).</param>
        public static void Complete(DialogueConfig cfg, string model, List<ChatMessage> messages, int maxTokens,
            Action<string> onOk, Action<string> onError, bool jsonMode, float temperatureOverride)
        {
            // copy everything we need: the worker thread must not touch Unity objects
            string baseUrl = (cfg.BaseUrl ?? "").Trim().TrimEnd('/');
            string key = (cfg.ApiKey ?? "").Trim();
            string effort = cfg.ReasoningEffort;
            string tokensField = string.IsNullOrWhiteSpace(cfg.MaxTokensField) ? "max_tokens" : cfg.MaxTokensField.Trim();
            float temperature = float.IsNaN(temperatureOverride) ? cfg.Temperature : temperatureOverride;
            bool local = IsLocalQwen(cfg, model);   // v0.8.0: steadier sampling for local qwen (Chinese drift)
            if (local && temperature < 0f) temperature = cfg.LocalTemperature;
            float topP = cfg.TopP >= 0f ? cfg.TopP : local ? 0.9f : -1f;
            float repeat = cfg.RepeatPenalty >= 0f ? cfg.RepeatPenalty : local ? 1.1f : -1f;
            int timeoutMs = Math.Max(5, cfg.TimeoutSeconds) * 1000;
            var headers = new Dictionary<string, string>(cfg.ExtraHeaders ?? new Dictionary<string, string>());
            bool debug = cfg.DebugLogPrompts;
            string modelName = model;
            bool localUrl = Main.IsLocalUrl(baseUrl);

            // v0.8.2-alpha: the server was just found down -> fail at once with the same clear message
            // (no second wait for the classifier + reply pair, no repeated connection attempts).
            string down = DownMessage(baseUrl);
            if (down != null) { Main.RunOnMainThread(() => onError?.Invoke(down)); return; }

            Task.Run(() =>
            {
                string result = null, error = null;
                try
                {
                    if (localUrl) Probe(baseUrl);   // v0.8.2-alpha: 2 s check that Ollama / LM Studio is listening
                    result = Send(baseUrl, key, headers, BuildBody(model, messages, maxTokens, tokensField, effort, temperature, true, jsonMode, topP, repeat), timeoutMs, debug);
                }
                catch (LlmHttpException e) when (e.Status == 400 && LooksLikeParamError(e.Body))
                {
                    // Some backends reject reasoning_effort / max_completion_tokens / temperature: retry once with the bare minimum.
                    try
                    {
                        result = Send(baseUrl, key, headers, BuildBody(model, messages, maxTokens, "max_tokens", null, -1f, false, false), timeoutMs, debug);
                    }
                    catch (Exception e2) { error = Describe(e2, baseUrl, modelName, key, timeoutMs, localUrl); }
                }
                catch (Exception e) { error = Describe(e, baseUrl, modelName, key, timeoutMs, localUrl); }

                if (error == null && string.IsNullOrWhiteSpace(result))
                    error = "Resposta vazia do modelo (empty reply).";
                Main.RunOnMainThread(() =>
                {
                    if (error != null) onError?.Invoke(error); else onOk?.Invoke(result.Trim());
                });
            });
        }

        static readonly System.Text.RegularExpressions.Regex LeakRx = new System.Text.RegularExpressions.Regex(
            @"(<\|im_|\n\s*(user|assistant|system|usu[aá]rio|jogador|player)\s*[:\n]|\n\s*\[[^\]\n]{0,60}\b(ano|year)\s*-?\d+[^\]\n]*\]|\n\s*\[Deus|\n\s*[\p{L}' -]{1,30} \(voz do c[eé]u\)\s*:|\n\s*[\p{L}' -]{1,30} \(voice from the sky\)\s*:|\n\s*[\p{L}' -]{1,30} \((em pessoa|in person)\)\s*:)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>v7: cut a reply at the first leaked role marker / transcript line and trim trailing junk.</summary>
        static readonly System.Text.RegularExpressions.Regex OtherSpeakerRx = new System.Text.RegularExpressions.Regex(
            @"\n\s*\**([A-ZÀ-Ý][\wÀ-ÿ'\-]{1,24})\**\s*:", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <param name="npcName">v7: when given, also cut at a later line where ANOTHER character starts speaking ("Raar: ...").</param>
        public static string CleanReply(string reply, string npcName = null)
        {
            if (string.IsNullOrEmpty(reply)) return reply;
            string r = reply.Replace("\r", "");
            var m = LeakRx.Match(r);
            bool cut = false;
            if (m.Success && m.Index > 0) { r = r.Substring(0, m.Index); cut = true; }
            if (!string.IsNullOrEmpty(npcName))
                foreach (System.Text.RegularExpressions.Match o in OtherSpeakerRx.Matches(r))
                    if (o.Index > 0 && !string.Equals(o.Groups[1].Value, npcName.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
                    { r = r.Substring(0, o.Index); cut = true; break; }
            int im = r.IndexOf("<|", StringComparison.Ordinal); if (im > 0) { r = r.Substring(0, im); cut = true; }
            if (cut) r = System.Text.RegularExpressions.Regex.Replace(r, @"[\s\?\uFFFD]*(\?\?|\uFFFD)[\s\?\uFFFD]*$", "");     // "...a??" garbage
            r = System.Text.RegularExpressions.Regex.Replace(r, @"[\s\[\(\-:|,;]+$", "");
            r = r.Trim();
            return r.Length > 0 ? r : reply.Trim();
        }

        /// <summary>v0.8.0: a local Ollama / qwen backend (gets temperature 0.6, top_p 0.9, repeat_penalty 1.1 unless configured).</summary>
        public static bool IsLocalQwen(DialogueConfig cfg, string model)
        {
            string m = (model ?? "").ToLowerInvariant(), u = (cfg?.BaseUrl ?? "").ToLowerInvariant();
            return m.Contains("qwen") || u.Contains(":11434") || u.Contains("ollama");
        }

        static string BuildBody(string model, List<ChatMessage> messages, int maxTokens, string tokensField,
            string effort, float temperature, bool optional, bool jsonMode, float topP = -1f, float repeat = -1f)
        {
            var arr = new JArray();
            foreach (var m in messages) arr.Add(new JObject { ["role"] = m.Role, ["content"] = m.Content });
            var body = new JObject { ["model"] = model, ["messages"] = arr };
            if (maxTokens > 0) body[tokensField] = maxTokens;
            if (optional)
            {
                if (!string.IsNullOrWhiteSpace(effort)) body["reasoning_effort"] = effort.Trim();
                if (temperature >= 0f) body["temperature"] = temperature;
                if (topP > 0f) body["top_p"] = topP;
                if (repeat > 0f)
                {   // Ollama's OpenAI-compatible endpoint ignores unknown fields; native "options" carried too
                    body["repeat_penalty"] = repeat;
                    body["options"] = new JObject { ["repeat_penalty"] = repeat };
                }
                if (jsonMode) body["response_format"] = new JObject { ["type"] = "json_object" };
                // v7: stop the model before it starts writing the next transcript turn (qwen leaked "\nuser\n[Deus ...").
                else body["stop"] = new JArray("\nuser", "\nassistant", "[Deus", "<|im_");
            }
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        static string Send(string baseUrl, string key, Dictionary<string, string> headers, string json, int timeoutMs, bool debug)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new Exception("base_url está vazio no config.json.");
            var req = (HttpWebRequest)WebRequest.Create(baseUrl + "/chat/completions");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;
            foreach (var h in headers) req.Headers[h.Key] = h.Value;
            if (debug) Main.RunOnMainThread(() => Main.LogInfo("[GodBox] request: " + json));

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            req.ContentLength = bytes.Length;
            using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

            string text;
            int status;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)resp.StatusCode;
                    text = r.ReadToEnd();
                }
            }
            catch (WebException we) when (we.Response is HttpWebResponse er)
            {
                string errBody = "";
                try { using (var r = new StreamReader(er.GetResponseStream(), Encoding.UTF8)) errBody = r.ReadToEnd(); } catch { }
                throw new LlmHttpException((int)er.StatusCode, errBody);
            }
            if (debug) Main.RunOnMainThread(() => Main.LogInfo("[GodBox] response: " + text));
            if (status < 200 || status >= 300) throw new LlmHttpException(status, text);
            return ExtractContent(text);
        }

        static string ExtractContent(string text)
        {
            var o = JObject.Parse(text);
            var msg = o["choices"]?[0]?["message"];
            var content = msg?["content"];
            if (content == null || content.Type == JTokenType.Null) return null;
            if (content.Type == JTokenType.String) return (string)content;
            // some servers return content as an array of parts
            var sb = new StringBuilder();
            foreach (var part in content) sb.Append((string)part["text"] ?? "");
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------------------
        // v0.8.2-alpha: friendly handling when the AI server is missing (Ollama not running, model not pulled,
        // no key, no internet). Each player runs their own server/key; nothing here is shared between players.
        // ---------------------------------------------------------------------------------------------------
        class BackendDownException : Exception { public BackendDownException(string m) : base(m) { } }

        static readonly object DownLock = new object();
        static string _downUrl, _downMsg;
        static DateTime _downUntil = DateTime.MinValue;
        const double DownSeconds = 8;

        /// <summary>The cached "server is down" message for this base_url, or null.</summary>
        static string DownMessage(string baseUrl)
        {
            lock (DownLock)
                return _downMsg != null && _downUrl == baseUrl && DateTime.UtcNow < _downUntil ? _downMsg : null;
        }

        /// <summary>Forget a cached "server down" (Recarregar config).</summary>
        public static void ResetDown() { lock (DownLock) _downMsg = null; }

        static void MarkDown(string baseUrl, string msg)
        {
            lock (DownLock) { _downUrl = baseUrl; _downMsg = msg; _downUntil = DateTime.UtcNow.AddSeconds(DownSeconds); }
        }

        /// <summary>True when the error text is one of the "server unreachable" messages (the caller can skip a second request).</summary>
        public static bool IsUnreachable(string error) =>
            error != null && (error.StartsWith(OllamaDownPrefix, StringComparison.Ordinal) || error.StartsWith(NoConnPrefix, StringComparison.Ordinal));
        const string OllamaDownPrefix = "O Ollama não está rodando.", NoConnPrefix = "Sem conexão com o servidor de IA";

        /// <summary>Quick TCP check of a local server (2 s), so a stopped Ollama fails at once instead of after timeout_seconds.</summary>
        static void Probe(string baseUrl)
        {
            Uri u;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out u)) return;
            string host = u.Host.Trim('[', ']');
            if (host == "0.0.0.0" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";
            using (var c = new TcpClient(host.Contains(":") ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork))
            {
                bool ok;
                try { var ar = c.BeginConnect(host, u.Port, null, null); ok = ar.AsyncWaitHandle.WaitOne(2000) && c.Connected; if (ok) c.EndConnect(ar); }
                catch (Exception) { ok = false; }
                if (!ok) throw new BackendDownException("probe failed");
            }
        }

        static string HostOf(string baseUrl)
        {
            Uri u;
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out u) ? u.Host : (baseUrl ?? "");
        }

        static bool LooksLikeParamError(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            string b = body.ToLowerInvariant();
            return b.Contains("unsupported") || b.Contains("unrecognized") || b.Contains("unknown") ||
                   b.Contains("reasoning_effort") || b.Contains("max_completion_tokens") || b.Contains("temperature") ||
                   b.Contains("extra inputs") || b.Contains("response_format") || b.Contains("not permitted") ||
                   b.Contains("top_p") || b.Contains("repeat_penalty") || b.Contains("options");
        }

        static string Describe(Exception e, string baseUrl, string model, string key, int timeoutMs, bool local)
        {
            if (e is AggregateException ae && ae.InnerException != null) return Describe(ae.InnerException, baseUrl, model, key, timeoutMs, local);
            var we = e as WebException;
            bool socket = false;
            for (var x = e; x != null; x = x.InnerException) if (x is SocketException) socket = true;
            // any transport failure without an HTTP answer (refused, DNS, TLS, reset) other than a timeout = server unreachable
            bool unreachable = e is BackendDownException || socket || (we != null && we.Response == null && we.Status != WebExceptionStatus.Timeout);
            if (unreachable)
            {
                string m = local
                    ? OllamaDownPrefix + " Baixe em https://ollama.com/download, abra-o e rode: ollama pull " + model + ". O Ollama não precisa de chave de API."
                    : NoConnPrefix + " (" + HostOf(baseUrl) + "). Confira a internet e o \"base_url\" no config.json.";
                MarkDown(baseUrl, m);
                return m;
            }
            if (we != null && we.Response == null && we.Status == WebExceptionStatus.Timeout)
                return "O modelo não respondeu em " + (timeoutMs / 1000) + " s. " + (local ? "Na primeira mensagem o Ollama ainda carrega o modelo (pode demorar); tente de novo. " : "Tente de novo. ") +
                       "Se acontecer sempre, aumente \"timeout_seconds\" no config.json. (EN: The model timed out; try again or raise \"timeout_seconds\".)";
            if (e is LlmHttpException h)
            {
                string msg = h.Body, code = "", type = "";
                try
                {
                    var err = JObject.Parse(h.Body)["error"];
                    if (err is JObject)
                    {
                        msg = (string)err["message"] ?? h.Body;
                        code = ((string)err["code"] ?? "").ToLowerInvariant();
                        type = ((string)err["type"] ?? "").ToLowerInvariant();
                    }
                    else if (err != null) msg = err.ToString();
                }
                catch { }
                if (msg != null && msg.Length > 300) msg = msg.Substring(0, 300) + "...";
                string all = (code + " " + type + " " + (msg ?? "")).ToLowerInvariant();
                string friendly;
                if (all.Contains("insufficient_quota") || all.Contains("exceeded your current quota") || all.Contains("billing") || all.Contains("credit") || h.Status == 402)
                    friendly = "Sua conta da API está sem créditos/cota. Adicione saldo no painel do provedor (OpenAI: platform.openai.com > Billing) e tente de novo. Não é um erro do mod.";
                else if (h.Status == 429)
                    friendly = "Muitas requisições seguidas (limite de taxa da API). Espere alguns segundos e tente de novo.";
                else if (h.Status == 404 && (local || all.Contains("pull")) && all.Contains("not found"))
                    return "O modelo " + model + " não foi baixado. Rode: ollama pull " + model + " (Ollama: https://ollama.com/download). O Ollama não precisa de chave de API.";
                else if ((h.Status == 401 || h.Status == 403) && string.IsNullOrEmpty(key))
                    friendly = "Falta a chave da API: este servidor (" + HostOf(baseUrl) + ") pede \"api_key\" no config.json. Ou use o Ollama local (padrão; veja a página da Oficina). " +
                               "(EN: Missing API key in config.json, or use local Ollama.)";
                else if (h.Status == 401 || h.Status == 403)
                    friendly = "Chave da API inválida, ausente ou sem permissão. Confira \"api_key\" no config.json e clique em Recarregar config.";
                else if (h.Status == 404)
                    friendly = "Modelo ou endereço (base_url) não encontrado. Confira \"model\" e \"base_url\" no config.json.";
                else if (h.Status >= 500)
                    friendly = "O servidor da API está com problemas agora. Tente de novo daqui a pouco.";
                else
                    friendly = "A API recusou o pedido.";
                return friendly + " [HTTP " + h.Status + (code.Length > 0 ? ", " + code : "") + ": " + msg + "]";
            }
            return e.GetType().Name + ": " + e.Message;
        }

        class LlmHttpException : Exception
        {
            public readonly int Status;
            public readonly string Body;
            public LlmHttpException(int status, string body) : base("HTTP " + status) { Status = status; Body = body; }
        }
    }
}
