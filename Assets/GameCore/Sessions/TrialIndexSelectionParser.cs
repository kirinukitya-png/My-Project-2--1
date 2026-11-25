using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ProjectNST.GameCore.Sessions
{
    /// <summary>Parses stable zero-based Trial indices and inclusive ranges.</summary>
    public static class TrialIndexSelectionParser
    {
        public const int MaximumExpandedIndices = 100000;
        public static bool TryParse(string text, out IReadOnlyList<long> indices, out string error)
        {
            indices = Array.AsReadOnly(Array.Empty<long>()); error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "At least one non-negative TrialIndex is required."; return false; }
            var output = new List<long>(); var seen = new HashSet<long>();
            foreach (string item in text.Split(','))
            {
                string token=item.Trim(); if(token.Length==0){error="TrialIndex list contains an empty entry.";return false;}
                int dash=token.IndexOf('-');
                if(dash<0){if(!Parse(token,out long n)){error="Invalid or negative TrialIndex: "+token;return false;} Add(n,seen,output);}
                else
                {
                    if(dash==0||dash==token.Length-1||token.IndexOf('-',dash+1)>=0||!Parse(token.Substring(0,dash).Trim(),out long first)||!Parse(token.Substring(dash+1).Trim(),out long last)||last<first){error="Invalid inclusive TrialIndex range: "+token;return false;}
                    ulong count=checked((ulong)(last-first)+1UL); if(count>MaximumExpandedIndices||(ulong)output.Count+count>MaximumExpandedIndices){error="TrialIndex selection exceeds safety limit "+MaximumExpandedIndices+".";return false;}
                    for(long i=first;;i++){Add(i,seen,output);if(i==last)break;}
                }
                if(output.Count>MaximumExpandedIndices){error="TrialIndex selection exceeds safety limit "+MaximumExpandedIndices+".";return false;}
            }
            indices=new ReadOnlyCollection<long>(output);return true;
        }
        private static bool Parse(string s,out long n)=>long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>=0;
        private static void Add(long n,HashSet<long> seen,List<long> output){if(seen.Add(n))output.Add(n);}
    }
}
