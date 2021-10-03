using System.Collections.Generic;
using TemplateEngine;

namespace AcceleratorConverter
{
    public class Option
    {
        public string Name { get; set; }
        public bool Display { get; set; }
        public string Label { get; set; }
        public string DataType { get; set; }
        public string Description { get; set; }
        public string InputType { get; set; }
        public string DefaultValue { get; set; }
        public bool? Required { get; set; }
        public List<Choice> Choices { get; set; }
    }
}