using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CryptoTrading.App.Core
{
    public class OrderedFixedLengthList : IList<double>
    {
        private List<double> _list;
        public OrderedFixedLengthList(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep;
            _list = new List<double>();
        }

        public double this[int index] { get => _list[index]; set => _list[index] = value; }

        public int Count => _list.Count;

        public bool IsReadOnly => false;

        public int NumberOfCandleSticksToKeep { get; }
        public double Current { get => _list.Last(); }

        public void Add(double item)
        {
            _list.Add(item);
            
            if (_list.Count >= NumberOfCandleSticksToKeep)
            {
                _list.RemoveAt(0);
;           }
        }
        public void Add(decimal item) => Add(Convert.ToDouble(item));

        public void Clear() => _list.Clear();

        public void AddRange(IEnumerable<decimal> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        public bool Contains(double item) => _list.Contains(item);

        public void CopyTo(double[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

        public IEnumerator<double> GetEnumerator() => _list.GetEnumerator();

        public int IndexOf(double item) => _list.IndexOf(item);

        public void Insert(int index, double item)
        {
            if (_list.Count == NumberOfCandleSticksToKeep)
            {
                _list.RemoveAt(0);
                _list.Insert(index - 1, item);
            }
        }

        public bool Remove(double item) => _list.Remove(item);

        public void RemoveAt(int index) => _list.RemoveAt(index);

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
