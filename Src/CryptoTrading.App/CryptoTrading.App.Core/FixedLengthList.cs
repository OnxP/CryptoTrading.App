using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public class OrderedFixedLengthList : IList<double>
    {
        public OrderedFixedLengthList(int numberOfCandleSticksToKeep)
        {
            NumberOfCandleSticksToKeep = numberOfCandleSticksToKeep;
        }

        public double this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public int Count => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public int NumberOfCandleSticksToKeep { get; }
        public double Current { get; set; }

        public void Add(double item)
        {
            throw new NotImplementedException();
        }
        public void Add(decimal item)
        {
            Add(Convert.ToDouble(item));
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public void AddRange(IEnumerable<decimal> items)
        {
            throw new NotImplementedException();
        }

        public bool Contains(double item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(double[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<double> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public int IndexOf(double item)
        {
            throw new NotImplementedException();
        }

        public void Insert(int index, double item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(double item)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
