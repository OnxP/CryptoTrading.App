using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core
{
    public class OrderedFixedLengthList<T> : IList<T>
    {
        private List<T> _list;
        public OrderedFixedLengthList(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep;
            _list = new List<T>();
        }

        public T this[int index] { get => _list[index]; set => _list[index] = value; }

        public int Count => _list.Count;

        public bool IsReadOnly => false;

        public int NumberOfCandleSticksToKeep { get; }
        public T Current { get => _list.Last(); }

        public void Add(T item)
        {
            _list.Add(item);

            if (_list.Count >= NumberOfCandleSticksToKeep)
            {
                _list.RemoveAt(0);
            }
        }
        //public void Add(decimal item) => Add(Convert.ToDouble(item));

        public void Clear() => _list.Clear();

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        public bool Contains(T item) => _list.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        public int IndexOf(T item) => _list.IndexOf(item);

        public void Insert(int index, T item)
        {
            if (_list.Count == NumberOfCandleSticksToKeep)
            {
                _list.RemoveAt(0);
                _list.Insert(index - 1, item);
            }
        }

        public bool Remove(T item) => _list.Remove(item);

        public void RemoveAt(int index) => _list.RemoveAt(index);

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
