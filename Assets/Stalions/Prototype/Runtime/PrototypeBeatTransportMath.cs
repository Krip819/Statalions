namespace Stalions.Prototype
{
    public static class PrototypeBeatTransportMath
    {
        public static long OrdinalAt(
            double dspTime,
            double epoch,
            double interval)
        {
            if (interval <= 0d || dspTime < epoch)
            {
                return -1L;
            }

            return (long)System.Math.Floor(
                (dspTime - epoch) / interval);
        }

        public static double BeatTime(
            double epoch,
            long ordinal,
            double interval)
        {
            return epoch + ordinal * interval;
        }

        public static int SlotForOrdinal(
            long ordinal,
            int slotCount)
        {
            if (ordinal < 0L || slotCount <= 0)
            {
                return -1;
            }

            return (int)(ordinal % slotCount);
        }
    }
}
