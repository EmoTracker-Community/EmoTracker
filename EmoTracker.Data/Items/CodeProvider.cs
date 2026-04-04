using System.Collections.Generic;

namespace EmoTracker.Data.Items
{
    public class CodeProvider
    {
        HashSet<string> mProvidedCodes = new HashSet<string>();

        public IEnumerable<string> ProvidedCodes
        {
            get { return mProvidedCodes; }
        }

        public bool Empty
        {
            get { return mProvidedCodes.Count > 0; }
        }

        public void Clear()
        {
            mProvidedCodes.Clear();
        }

        public void AddCodes(string spec)
        {
            if (!string.IsNullOrWhiteSpace(spec))
            {
                string[] codes = spec.Split(',');
                foreach (string rawCode in codes)
                {
                    string code = rawCode.Trim().ToLower();
                    if (!mProvidedCodes.Contains(code))
                        mProvidedCodes.Add(code);
                }
            }
        }

        public bool ProvidesCode(string code)
        {
            if (!string.IsNullOrWhiteSpace(code))
                return mProvidedCodes.Contains(code.Trim().ToLower());

            return false;
        }
    }
}
