using System.Collections.Generic;
using System.Linq;

namespace TemplateEngine
{
    public static class IceExtensions
    {
        public static IEnumerable<string> AsEnumerable(this Ice? ice)
        {
            if (!ice.HasValue)
                yield break;
            if (ice.Value.StringArray.Any())
            {
                foreach (var item in ice.Value.StringArray)
                    yield return item;
                yield break;
            }

            if(ice.Value.String != null)
                yield return ice.Value.String;
        }

    }
}