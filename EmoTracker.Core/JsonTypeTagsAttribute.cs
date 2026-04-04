using System;
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


        public static T CreateIntanceForTypeTag<T>(string type)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(type))
                return null;

            type = type.Trim();

            foreach (Type itemType in TypeRegistry<T>.SupportRegistry)
            {
                object[] disallowAttrs = itemType.GetCustomAttributes(typeof(DisallowCreationFromTagAttribute), false);
                if (disallowAttrs != null && disallowAttrs.Length > 0)
                    continue;

                object[] attrs = itemType.GetCustomAttributes(typeof(JsonTypeTagsAttribute), false);
                foreach (JsonTypeTagsAttribute tagAttr in attrs)
                {
                    foreach (string supportedTag in tagAttr.TypeTags)
                    {
                        if (string.Equals(supportedTag, type, StringComparison.OrdinalIgnoreCase))
                        {
                            return Activator.CreateInstance(itemType) as T;
                        }
                    }
                }
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
