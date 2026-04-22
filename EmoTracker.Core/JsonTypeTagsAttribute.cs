using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DisallowCreationFromTagAttribute : Attribute
    {
    }


    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class JsonTypeTagsAttribute : Attribute
    {
        string[] mTags;

        public JsonTypeTagsAttribute(params string[] tags)
        {
            mTags = tags.ToArray();
        }

        public IEnumerable<string> TypeTags
        {
            get { return mTags; }
        }

        private static readonly ConcurrentDictionary<Type, Dictionary<string, Type>> sTagCachePerType = new();

        private static Dictionary<string, Type> GetOrCreateTagCache(Type targetType)
        {
            return sTagCachePerType.GetOrAdd(targetType, static t =>
            {
                var cache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                var registryType = typeof(TypeRegistry<>).MakeGenericType(t);
                var registryProp = registryType.GetProperty("SupportRegistry");
                var registryList = registryProp?.GetValue(null) as IEnumerable<Type> ?? Array.Empty<Type>();
                foreach (Type itemType in registryList)
                {
                    var disallowAttrs = itemType.GetCustomAttributes(typeof(DisallowCreationFromTagAttribute), false);
                    if (disallowAttrs != null && disallowAttrs.Length > 0)
                        continue;

                    var attrs = itemType.GetCustomAttributes(typeof(JsonTypeTagsAttribute), false);
                    foreach (JsonTypeTagsAttribute tagAttr in attrs)
                    {
                        foreach (string tag in tagAttr.TypeTags)
                        {
                            cache[tag] = itemType;
                        }
                    }
                }
                return cache;
            });
        }

        public static T CreateInstanceForTypeTag<T>(string type)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(type))
                return null;

            var cache = GetOrCreateTagCache(typeof(T));
            if (cache.TryGetValue(type.Trim(), out var targetType))
            {
                return Activator.CreateInstance(targetType) as T;
            }
            return null;
        }

        public static string GetDefaultTagForType(Type type)
        {
            if (type == null)
                return null;

            object[] attrs = type.GetCustomAttributes(typeof(JsonTypeTagsAttribute), false);
            foreach (JsonTypeTagsAttribute tagAttr in attrs)
            {
                return tagAttr.TypeTags.FirstOrDefault();
            }

            return null;
        }

        public static bool GetTypeSupportsTag(Type type, string tag)
        {
            if (type == null)
                return false;

            if (string.IsNullOrWhiteSpace(tag))
                return false;

            object[] attrs = type.GetCustomAttributes(typeof(JsonTypeTagsAttribute), false);
            foreach (JsonTypeTagsAttribute tagAttr in attrs)
            {
                foreach (string supportedTag in tagAttr.TypeTags)
                {
                    if (string.Equals(supportedTag, tag, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
