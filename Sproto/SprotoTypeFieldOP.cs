using System;

namespace Sproto
{
    public class SprotoTypeFieldOP
    {
        private const int SlotBitsSize = sizeof(UInt32) * 8;
        public UInt32[] has_bits;

        public SprotoTypeFieldOP(int maxFieldCount)
        {
            int slotCount = maxFieldCount / SlotBitsSize;
            if (maxFieldCount % SlotBitsSize > 0)
                slotCount++;
            has_bits = new UInt32[slotCount];
        }

        private int GetArrayIndex(int bitIdx)
            => bitIdx / SlotBitsSize;

        private int GetSlotbitIndex(int bitIdx)
            => bitIdx % SlotBitsSize;

        public bool has_field(int fieldIdx)
        {
            int arrayIdx = GetArrayIndex(fieldIdx);
            int slotbitIdx = GetSlotbitIndex(fieldIdx);
            return Convert.ToBoolean(has_bits[arrayIdx] & (UInt32)(1 << slotbitIdx));
        }

        public void set_field(int fieldIdx, bool isHas)
        {
            int arrayIdx = GetArrayIndex(fieldIdx);
            int slotbitIdx = GetSlotbitIndex(fieldIdx);

            if (isHas)
                has_bits[arrayIdx] |= (UInt32)(1 << slotbitIdx);
            else
                has_bits[arrayIdx] &= ~((UInt32)(1 << slotbitIdx));
        }

        public void clear_field()
        {
            for (int i = 0; i < has_bits.Length; i++)
                has_bits[i] = 0;
        }
    }
}
