using System;
using System.Collections.Generic;

namespace AcceleratorConverter
{
    public class Substitution
    {
        private sealed class TextWithEqualityComparer : IEqualityComparer<Substitution>
        {
            public bool Equals(Substitution x, Substitution y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Text == y.Text && x.With == y.With;
            }

            public int GetHashCode(Substitution obj)
            {
                return HashCode.Combine(obj.Text, obj.With);
            }
        }

        public static IEqualityComparer<Substitution> Comparer { get; } = new TextWithEqualityComparer();

        public string Text { get; set; }
        public string With { get; set; }
    }
}