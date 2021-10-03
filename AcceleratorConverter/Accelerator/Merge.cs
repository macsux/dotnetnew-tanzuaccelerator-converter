using System.Collections.Generic;

namespace AcceleratorConverter
{
    public class Merge
    {
        public List<string> Include { get; set; }
        public List<string> Exclude { get; set; }
        public string Condition { get; set; }
        public List<Transform> Chain { get; set; }
    }
}