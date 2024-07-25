using System;
using System.Collections.Generic;

using Newtonsoft.Json;

using GLTF2BIM.GLTF.Extensions.BIM.BaseTypes;

namespace GLTF2BIM.GLTF.Extensions.BIM.Containers
{
    [Serializable]
    public class glTFBIMPropertyContainer : glTFBIMContainer
    {
        private string _uri;
        private glTFBIMPropertyData _propData = new glTFBIMPropertyData();

        public glTFBIMPropertyContainer(string uri)
        {
            _uri = uri;
        }

        [JsonProperty("$type")]
        public override string Type => "properties";

        [JsonProperty("uri")]
        public override string Uri => _uri;

        public bool HasPropertyData = true;

        public void Record(string id, Dictionary<string, object> props) => _propData.Record(id, props);

        public string Pack()
        {
            return JsonConvert.SerializeObject(
                    _propData,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    }
                );
        }

        [Serializable]
        private class glTFBIMPropertyData
        {
            [JsonProperty("records", Order = 1)]
            public Dictionary<string, HashSet<uint>> Records { get; set; } = new Dictionary<string, HashSet<uint>>();

            [JsonProperty("groups", Order = 2)]
            public List<glTFBIMPropertyDataGroup> Groups { get; set; } = new List<glTFBIMPropertyDataGroup>();

            [JsonProperty("keys", Order = 3)]
            public List<string> Keys { get; set; } = new List<string>();

            [JsonProperty("values", Order = 4)]
            public List<object> Values { get; set; } = new List<object>();

            public void Record(string id, Dictionary<string, object> props)
            {
                // add properties and group
                var grp = new glTFBIMPropertyDataGroup();

                foreach (var propData in props)
                {
                    if (propData.Value is null)
                        continue;

                    // add key
                    if (Keys.IndexOf(propData.Key) is int keyIdx && keyIdx != -1)
                    {
                        grp.Keys.Add((uint)keyIdx);
                    }
                    else
                    {
                        Keys.Add(propData.Key);
                        grp.Keys.Add((uint)Keys.Count - 1);
                    }

                    // add value
                    if (Values.IndexOf(propData.Value) is int valueIdx && valueIdx != -1)
                    {
                        grp.Values.Add((uint)valueIdx);
                    }
                    else
                    {
                        Values.Add(propData.Value);
                        grp.Values.Add((uint)Values.Count - 1);
                    }
                }

                uint groupIndex = 0;
                if (Groups.IndexOf(grp) is int grpIndex && grpIndex > -1)
                    groupIndex = (uint)grpIndex;
                else
                {
                    Groups.Add(grp);
                    groupIndex = (uint)Groups.Count - 1;
                }

                if (Records.TryGetValue(id, out var groupIndices))
                {
                    groupIndices.Add(groupIndex);
                }
                else
                {
                    Records.Add(id, new HashSet<uint> { groupIndex });
                }
            }
        }

        [Serializable]
        private class glTFBIMPropertyDataGroup
        {
            [JsonProperty("keys", Order = 1)]
            public List<uint> Keys { get; set; } = new List<uint>();

            [JsonProperty("values", Order = 2)]
            public List<uint> Values { get; set; } = new List<uint>();

            public override bool Equals(object obj)
            {
                if (obj is glTFBIMPropertyDataGroup other)
                    return Keys.Equals(other.Keys) && Values.Equals(other.Values);
                return false;
            }

            public override int GetHashCode() => base.GetHashCode();
        }
    }
}