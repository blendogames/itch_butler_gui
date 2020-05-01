using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace itch_butler_gui
{
    class ProjectProfile
    {
        [JsonProperty("profilename")]
        public string profilename { get; set; }

        [JsonProperty("username")]
        public string username { get; set; }

        [JsonProperty("gamename")]
        public string gamename { get; set; }

        [JsonProperty("arguments")]
        public string arguments { get; set; }

        [JsonProperty("builds")]
        public ProfileBuilds[] builds { get; set; }

        public ProjectProfile()
        {
        }
    }

    class ProfileBuilds
    {
        [JsonProperty("platform")]
        public string platform { get; set; }

        [JsonProperty("folder")]
        public string folder { get; set; }

        public ProfileBuilds()
        {
        }
    }
}
