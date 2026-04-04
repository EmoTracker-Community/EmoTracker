using EmoTracker.Data.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data
{
    public interface ICodeProvider
    {
        object FindObjectForCode(string code);

        uint ProviderCountForCode(string code, out AccessibilityLevel maxAccessibility);
    }
}
