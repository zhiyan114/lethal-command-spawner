using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Unity.Netcode;

#if DEBUG || TESTPLAY
namespace SentryUtils
{
    public static class SDKINFO
    {
        public const string SDKNAME = "BepInExSentryUwU/zhiyan114";
        public const string SDKVER = "1.0.0";
    }
    class SentryLogItem
    {
        public long timestamp { get; set; }
        public string trace_id { get; set; } = "";
        public string level { get; set; } = "";
        public string body { get; set; } = "";
        public Dictionary<string, Dictionary<string, object>> attributes { get; set; }

        internal SentryLogItem(string level, string body, Dictionary<string, Dictionary<string, object>>? attributes)
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            trace_id = Guid.NewGuid().ToString();
            this.level = level;
            this.body = body;
            this.attributes = attributes ?? new Dictionary<string, Dictionary<string, object>>();


            // Attach SDK Info
            this.attributes.Add("sentry.sdk.name", new Dictionary<string, object>
            {
                ["value"] = SDKINFO.SDKNAME,
                ["type"] = "string"
            });
            this.attributes.Add("sentry.sdk.version", new Dictionary<string, object>
            {
                ["value"] = SDKINFO.SDKVER,
                ["type"] = "string"
            });
        }
    }

    class HttpHandle
    {
        private HttpClient WebClient = new HttpClient();
        private readonly string? DSNURL;
        private readonly string? DSNEnvUrl;
        private readonly string? DSNAuthHeader;
        private readonly string? SpotlightURL;
        internal HttpHandle(string? DSNURL, string? SpotlightURL)
        {
            if (DSNURL == null && SpotlightURL == null)
                throw new Exception("HttpHandle: DSN and Spotlight URL is null. At least one is required.");
            if (DSNURL != null)
            {
                Uri DSNUri = new Uri(DSNURL);
                string PubKey = DSNUri.UserInfo;
                string Host = DSNUri.Host;
                string ProjID = DSNUri.AbsolutePath.Trim('/');
                DSNEnvUrl = $"https://{Host}/api/{ProjID}/envelope/";
                DSNAuthHeader = $"Sentry sentry_version=7, sentry_key={PubKey}, sentry_client={SDKINFO.SDKNAME}/{SDKINFO.SDKVER}";
            }
            this.DSNURL = DSNURL;
            this.SpotlightURL = SpotlightURL;
        }

        public async Task sendRequest(IReadOnlyList<SentryLogItem> Items)
        {
            StringContent netContent = new StringContent(PrepareBody(Items), Encoding.UTF8, "application/x-sentry-envelope");

            // Fire the request for spotlight first
            if(SpotlightURL != null)
                await WebClient.PostAsync(SpotlightURL, netContent);

            // Prepare for sentry DSN endpoint and fire it there
            if(DSNURL != null)
            {
                netContent.Headers.Add("X-Sentry-Auth", DSNAuthHeader);
                await WebClient.PostAsync(DSNEnvUrl, netContent);
            }
        }

        private string PrepareBody(IReadOnlyList<SentryLogItem> Items)
        {
            Dictionary<string, object> EnvHeader = new Dictionary<string, object>()
            {
                ["sent_at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["sdk"] = new Dictionary<string, string>
                {
                    ["name"] = SDKINFO.SDKNAME,
                    ["version"] = SDKINFO.SDKVER
                }
            };
            Dictionary<string, object> ItemHeader = new Dictionary<string, object>()
            {
                ["type"] = "log",
                ["item_count"] = Items.Count,
                ["content_type"] = "application/vnd.sentry.items.log+json"
            };
            Dictionary<string, object> ItemContent = new Dictionary<string, object>()
            {
                ["version"] = 2,
                ["ingest_settings"] = new Dictionary<string,string>()
                {
                    ["infer_ip"] = "never", // Set to auto if you want to see player's IP address, but why??
                    ["infer_user_agent"] = "never", // Setting this to auto is not useful for anything
                },
                ["items"] = Items.ToArray(),
            };

            // Add any conditional key/value
            if (DSNURL != null)
                EnvHeader.Add("dsn", DSNURL);

            return $"{JsonConvert.SerializeObject(EnvHeader)}\n{JsonConvert.SerializeObject(ItemHeader)}\n{JsonConvert.SerializeObject(ItemContent)}";
        }
    }

    public class SentryLogger : ManualLogSource
    {
        private readonly HttpHandle ReqHandler;
        private readonly string sessionID;
        private readonly List<SentryLogItem> LogBuffer = new List<SentryLogItem>();
        private bool tableLocked = false;
        private readonly Timer timer = new Timer();
        public string sID { get {  return sessionID; }  }

        private SentryLogger(string sourceName, string? sentryDSN, string? sessionID, string? spotlightURL) : base(sourceName)
        {
            // Setup fields
            if (sentryDSN == null && spotlightURL == null) throw new Exception("SentryLogger: Require at least either sentryDSN or spotlightURL");
            ReqHandler = new HttpHandle(sentryDSN, spotlightURL);
            this.sessionID = sessionID ?? Guid.NewGuid().ToString();
            
            // Configure timer param
            timer.Enabled = false;
            timer.AutoReset = true;
            timer.Interval = 5000;
            timer.Elapsed += (a, b) => Flush();
        }

        /// <summary>
        /// Initialize BepInEx compatible logging system. Use session ID to help track of issues related to game session!
        /// </summary>
        /// <param name="SentryDSN">Sentry DSN to send the log data to</param>
        /// <param name="sourceName">BepInEx log source name</param>
        /// <param name="sessionID">optional session ID to track of game session for better diagnostic</param>
        /// <param name="spotlightURL">Sentry Spotlight URL (default: http://localhost:8969/stream) (use for local development only!)</param>
        /// <returns>self</returns>
        public static SentryLogger Initialize(string sourceName, string? SentryDSN, string? sessionID, string? spotlightURL)
        {
            SentryLogger logger = new SentryLogger(sourceName, SentryDSN, sessionID, spotlightURL);
            BepInEx.Logging.Logger.Sources.Add(logger);
            return logger;
        }
        public new void LogDebug(object data)
        {
            AddBuffer("debug", data, null);
            base.LogDebug(data);
        }
        public void LogDebug(object data, Dictionary<string, object> attributes)
        {
            AddBuffer("debug", data, attributes);
            base.LogDebug(data);
        }
        public new void LogInfo(object data)
        {
            AddBuffer("info", data, null);
            base.LogInfo(data);
        }
        public void LogInfo(object data, Dictionary<string, object> attributes)
        {
            AddBuffer("info", data, attributes);
            base.LogInfo(data);
        }
        public new void LogWarning(object data)
        {
            AddBuffer("warn", data, null);
            base.LogWarning(data);
        }
        public void LogWarning(object data, Dictionary<string, object> attributes)
        {
            AddBuffer("warn", data, attributes);
            base.LogWarning(data);
        }
        public new void LogError(object data)
        {
            AddBuffer("error", data, null);
            base.LogError(data);
        }
        public void LogError(object data, Dictionary<string, object> attributes)
        {
            AddBuffer("error", data, attributes);
            base.LogError(data);
        }
        public new void LogFatal(object data)
        {
            AddBuffer("fatal", data, null);
            base.LogFatal(data);
        }
        public void LogFatal(object data, Dictionary<string, object> attributes)
        {
            AddBuffer("fatal", data, attributes);
            base.LogFatal(data);
        }

        /// <summary>
        /// Process and add log items to buffer. This is internal use only.
        /// </summary>
        /// <param name="level">Sentry-defined log level string</param>
        /// <param name="data">Any object that is string convertable</param>
        /// <param name="attributes">Sentry-defined attribute values</param>
        private void AddBuffer(string level, object data, Dictionary<string, object>? attributes)
        {
            if (tableLocked) return;

            // Add useful attributes
            Dictionary<string, Dictionary<string, object>> procAttribute = new Dictionary<string, Dictionary<string, object>>();
            procAttribute.Add("sessionID", new Dictionary<string, object>
            {
                { "value", sessionID },
                { "type", "string" }
            });
            procAttribute.Add("instanceType", new Dictionary<string, object>()
            {
                ["value"] = NetworkManager.Singleton.IsServer ? "server" : "client",
                ["type"] = "string"
            });

            // Process customized attribute
            if (attributes != null)
            {
                foreach(string key in attributes.Keys)
                {
                    object item = attributes.GetValueSafe(key);
                    Type objType = item.GetType();
                    string itemType = "";

                    // Type to string
                    if (objType == typeof(bool))
                        itemType = "boolean";
                    else if (
                        objType == typeof(sbyte) ||
                        objType == typeof(byte) ||
                        objType == typeof(ushort) ||
                        objType == typeof(int) ||
                        objType == typeof(uint) ||
                        objType == typeof(long)
                      )
                        itemType = "integer";
                    else if (objType == typeof(float) || objType == typeof(double))
                        itemType = "double";
                    else if(objType.IsArray)
                        itemType = "array";
                    else
                        itemType = "string";

                    // Post-processing //

                    // Because sentry doesnt show array type yet, we'll process it to string instead
                    if(objType.IsArray)
                    {
                        item = JsonConvert.SerializeObject(item);
                        itemType = "string";
                    }

                    procAttribute.Add(key, new Dictionary<string, object>()
                    {
                        ["value"] = itemType == "string" ? item.ToString() : item,
                        ["type"] = itemType
                    });
                }
            }

            SentryLogItem Item = new SentryLogItem(level, data.ToString(), procAttribute);
            LogBuffer.Add(Item);

            if (LogBuffer.Count >= 50) {
                // Count based flush (sentry limit is 100)
                timer.Stop();
                Flush();
                return;
            }
            if (!timer.Enabled)
                timer.Start();

        }

        /// <summary>
        /// Prepare and send log data to remote endpoint (and spotlight instance, if configured)
        /// </summary>
        private async Task Flush()
        {
            tableLocked = true;
            timer.Stop();
            await ReqHandler.sendRequest(LogBuffer);
            LogBuffer.Clear();
            tableLocked = false;
        }
    }
}
#endif