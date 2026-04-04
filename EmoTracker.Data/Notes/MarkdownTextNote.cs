using EmoTracker.Core;
using System;
using System.Linq;
using System.Xml;

namespace EmoTracker.Data.Notes
{
    public class MarkdownTextNote : Note
    {
        string mMarkdownSource;

        [DependentProperty("MarkdownSourceEmpty")]
        public string MarkdownSource
        {
            get { return mMarkdownSource; }
            set { SetProperty(ref mMarkdownSource, value); }
        }

        public bool MarkdownSourceEmpty
        {
            get { return string.IsNullOrWhiteSpace(MarkdownSource); }
        }
    }
}
