using System.Collections.Generic;

namespace AcceleratorConverter
{
    public class Engine
    {
        public List<Let> Let { get; set; }
        public List<Merge> Merge { get; set; }
    }

    public class Let
    {
        public string Name { get; set; }
        public string Expression { get; set; }
    }
    
}