using System;
using System.Runtime.InteropServices;
using RGiesecke.DllExport;

namespace Vector
{
    public class Vector
    {
        [DllExport("VectorByCoordinates", CallingConvention.Cdecl)]
        public static double ByCoordinates(double x, double y, double z)
        {
            //return new Vector(x, y, z);
            return x + y + z;
        }

        private Vector(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [DllExport("VectorAdd", CallingConvention.Cdecl)]
        public Vector Add(Vector v)
        {
            return new Vector(X + v.X, Y + v.Y, Z + v.Z);
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }
}
