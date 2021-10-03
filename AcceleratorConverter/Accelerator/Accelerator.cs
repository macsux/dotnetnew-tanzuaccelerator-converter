using System.Collections.Generic;

namespace AcceleratorConverter
{
    public class Accelerator
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
        public List<string> Tags { get; set; }
        public List<Option> Options { get; set; }
        
    }
}