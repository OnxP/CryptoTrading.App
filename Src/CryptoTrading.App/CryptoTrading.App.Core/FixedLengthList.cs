using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public class FixedLengthList : IList<double>
    {
        public double this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public int Count { get; set; }

        public bool IsReadOnly => false;

        public void Add(double item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
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
