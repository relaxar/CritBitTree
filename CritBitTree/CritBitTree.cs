﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace CritBitTree
{
    [StructLayout(LayoutKind.Auto, Pack = 1)]
    internal class CritBitInternalNode : ICritBitNode
    {
        public ICritBitNode Child1;

        public ICritBitNode Child2;

        public int Byte;

        public byte Otherbits;
    }

    [StructLayout(LayoutKind.Auto, Pack = 1)]
    internal class CritBitExternalNode<T> : ICritBitNode
    {
        public ReadOnlyMemory<byte> Key;

        public T Value;
    }

    internal interface ICritBitNode
    {
    }

    public class CritBitTree<T> : IEnumerable<T>
    {
        private ICritBitNode _rootNode;

        [Pure]
        public bool ContainsKey(in ReadOnlySpan<byte> key)
        {
            return TryGetValue(key, out _);
        }

        public bool TryGetValue(in ReadOnlySpan<byte> key, out T value)
        {
            if (_rootNode == null)
            {
                value = default;
                return false;
            }

            CritBitExternalNode<T> externalNode = FindBestMatch(key);
            if (key.SequenceEqual(externalNode.Key.Span))
            {
                value = externalNode.Value;
                return true;
            }

            value = default;
            return false;
        }

        private CritBitExternalNode<T> FindBestMatch(in ReadOnlySpan<byte> key)
        {
            var node = _rootNode;
            var keyLength = key.Length;

            while (node is CritBitInternalNode internalNode)
            {
                byte c = 0;
                if (internalNode.Byte < keyLength)
                    c = key[internalNode.Byte];

                var direction = (1 + (internalNode.Otherbits | c)) >> 8;
                node = direction == 0 ? internalNode.Child1 : internalNode.Child2;
            }

            return (CritBitExternalNode<T>) node;
        }

        public bool Add(in ReadOnlyMemory<byte> key, T value)
        {
            var keyLength = key.Length;

            if (_rootNode == null)
            {
                var rootNode = new CritBitExternalNode<T>();
                rootNode.Key = key;
                rootNode.Value = value;
                _rootNode = rootNode;
                return true;
            }

            var keySpan = key.Span;
            var externalNode = FindBestMatch(keySpan);

#region Find the critical bit

            int pValueLength = externalNode.Key.Length;
            ReadOnlySpan<byte> pValue = externalNode.Key.Span;
            
            int newbyte;
            uint newotherbits = 0;
            bool differentByteFound = false;

            for (newbyte = 0; newbyte < keyLength; newbyte++)
            {
                if (newbyte >= pValueLength)
                {
                    newotherbits = keySpan[newbyte];
                    differentByteFound = true;
                    break;
                }

                if (pValue[newbyte] != keySpan[newbyte])
                {
                    newotherbits = (uint) (pValue[newbyte] ^ keySpan[newbyte]);
                    differentByteFound = true;
                    break;
                }
            }

            if (!differentByteFound)
                return false;

            newotherbits |= newotherbits >> 1;
            newotherbits |= newotherbits >> 2;
            newotherbits |= newotherbits >> 4;
            newotherbits = (newotherbits & ~ (newotherbits >> 1)) ^ 255;

            var c = pValueLength > newbyte ? pValue[newbyte] : (byte) 0;
            
            uint newdirection = (1 + (newotherbits | c)) >> 8;

#endregion
            
            var wherep = _rootNode;
            CritBitInternalNode parent = null;
            int parentDirection = -1;

            while (true)
            {
                var node = wherep;
                if (!(node is CritBitInternalNode internalNode))
                    break;
                
                if (internalNode.Byte > newbyte) break;
                if (internalNode.Byte == newbyte && internalNode.Otherbits > newotherbits) break;

                c = 0;
                if (internalNode.Byte < keyLength)
                    c = keySpan[internalNode.Byte];
                var direction = (1 + (internalNode.Otherbits | c)) >> 8;
                parent = internalNode;
                parentDirection = direction;
                wherep = direction == 0 ? internalNode.Child1 : internalNode.Child2;
            }

            var newExternalNode = new CritBitExternalNode<T>();
            newExternalNode.Key = key;
            newExternalNode.Value = value;

            var newNode = new CritBitInternalNode();
            if (newdirection == 0)
            { 
                newNode.Child1 = wherep;
                newNode.Child2 = newExternalNode;
            }
            else
            {
                newNode.Child1 = newExternalNode;
                newNode.Child2 = wherep;
            }
            newNode.Byte = newbyte;
            newNode.Otherbits = (byte)newotherbits;

            if (parent == null)
                _rootNode = newNode;
            else
            {
                if (parentDirection == 0)
                    parent.Child1 = newNode;
                else
                    parent.Child2 = newNode;
            }

            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_rootNode == null)
                yield break;

            foreach (var item in GetItems(_rootNode))
                yield return item;
        }

        private IEnumerable<T> GetItems(ICritBitNode node)
        {
            if (node is CritBitExternalNode<T> externalNode)
            {
                yield return externalNode.Value;
                yield break;
            }

            var internalNode = (CritBitInternalNode)node;
            foreach (var item in GetItems(internalNode.Child1))
            {
                yield return item;
            }

            foreach (var item in GetItems(internalNode.Child2))
            {
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Remove(in ReadOnlySpan<byte> key)
        {
            if (_rootNode == null)
                return false;

            var node = _rootNode;
            var keyLength = key.Length;
            ICritBitNode parent = null;
            int parentDirection = -1;
            ICritBitNode grandParent = null;
            int grandParentDirection = -1;

            while (node is CritBitInternalNode internalNode)
            {
                byte c = 0;
                if (internalNode.Byte < keyLength)
                    c = key[internalNode.Byte];

                var direction = (1 + (internalNode.Otherbits | c)) >> 8;
                grandParent = parent;
                grandParentDirection = parentDirection;
                parent = node;
                parentDirection = direction;
                node = direction == 0 ? internalNode.Child1 : internalNode.Child2;
            }

            var externalNode = (CritBitExternalNode<T>) node;

            if (!key.SequenceEqual(externalNode.Key.Span))
                return false;

            if (grandParent == null)
            {
                _rootNode = null;
                return true;
            }
            
            if (grandParentDirection == 0)
            { 
                if (parentDirection == 0)
                    ((CritBitInternalNode)grandParent).Child1 = ((CritBitInternalNode)parent).Child2;
                else
                    ((CritBitInternalNode)grandParent).Child1 = ((CritBitInternalNode)parent).Child1;
            }
            else
            {
                if (parentDirection == 0)
                    ((CritBitInternalNode)grandParent).Child2 = ((CritBitInternalNode)parent).Child2;
                else
                    ((CritBitInternalNode)grandParent).Child2 = ((CritBitInternalNode)parent).Child1;
            }

            return true;
        }
    }
}
