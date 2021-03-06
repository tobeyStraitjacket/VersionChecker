﻿#if SUBNAUTICA
using Oculus.Newtonsoft.Json;
#elif BELOWZERO
using Newtonsoft.Json;
#endif
using System.Net;
using System.Threading.Tasks;

namespace Straitjacket.Subnautica.Mods.VersionChecker.VersionCheckerAPI
{
    using Utility;

    internal class ModJson : INexusModJson
    {
        [JsonRequired, JsonProperty(PropertyName = "mod_id")]
        public int ModId { get; set; }

        [JsonRequired, JsonProperty(PropertyName = "game_id")]
        public int GameId { get; set; }

        [JsonRequired, JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonRequired, JsonProperty(PropertyName = "version")]
        public string Version { get; set; }

        [JsonRequired, JsonProperty(PropertyName = "status")]
        public string Status { get; set; }

        [JsonRequired, JsonProperty(PropertyName = "available")]
        public bool Available { get; set; }

        [JsonProperty(PropertyName = "domain_name")]
        public string DomainName { get; set; }

        public static async Task<ModJson> GetAsync(string domain, int id, string name, string qModId)
            => await Networking.ReadJsonAsync<ModJson>(GetUrl(domain, id, name, qModId));

        private static string GetUrl(string domain, int id, string name, string qModId)
            => "http://mods.vc.api.straitjacket.software/v1/" +
               $"games/{WebUtility.UrlEncode(domain)}/" +
               $"mods/{WebUtility.UrlEncode(id.ToString())}/" +
               $"{WebUtility.UrlEncode(name)}/" +
               $"{WebUtility.UrlEncode(qModId)}.json";
    }
}
