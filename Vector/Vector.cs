using System;
using System.Runtime.InteropServices;

namespace Vector
{
    public class Vector
    {
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

        public Vector Add(Vector v)
        {
            return new Vector(X + v.X, Y + v.Y, Z + v.Z);
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }
}
