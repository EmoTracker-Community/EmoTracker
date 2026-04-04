using Newtonsoft.Json.Linq;
using System;

namespace EmoTracker.Data.JSON
{
    public static class JsonUtility
    {
        public static string GetStringValue(this JObject token, string key)
        {
            JToken value;
            if (token.TryGetValue(key, out value) && value != null)
                return token.Value<string>();

            return null;
        }

        public static T GetValue<T>(this JObject token, string key, T defaultValue = default(T))
        {
            try
            {
                JToken value;
                if (token.TryGetValue(key, out value) && value != null)
                    return value.Value<T>();
            }
            catch
            {
            }

            return defaultValue;
        }

        public static T GetEnumValue<T>(this JObject token, string key, T defaultValue = default(T)) where T : struct
        {
            T result = defaultValue;

            JToken value;
            if (token.TryGetValue(key, out value) && value != null)
            {
                string strValue = value.Value<string>();
                Enum.TryParse<T>(strValue, true, out result);
            }

            return result;
        }

        public static T GetValue<T>(this JToken token, T defaultValue = default(T))
        {
            try
            {
                return token.Value<T>();
            }
            catch
            {
            }

            return defaultValue;
        }
    }
}
