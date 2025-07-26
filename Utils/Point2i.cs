namespace GitMC.Utils
{
    /// <summary>
    /// 2D coordinate point, used to represent chunk and region coordinates
    /// Based on https://zh.minecraft.wiki/w/%E5%8C%BA%E5%9F%9F%E6%96%87%E4%BB%B6%E6%A0%BC%E5%BC%8F coordinate system
    /// </summary>
    public struct Point2i : IEquatable<Point2i>
    {
        public int X { get; set; }
        public int Z { get; set; }

        public Point2i(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>
        /// Convert chunk coordinates to region coordinates
        /// Uses bit shift: regionX = chunkX >> 5, regionZ = chunkZ >> 5
        /// </summary>
        public Point2i ChunkToRegion()
        {
            return new Point2i(X >> 5, Z >> 5);
        }

        /// <summary>
        /// Convert region coordinates to the chunk coordinates of the top-left corner of the region
        /// </summary>
        public Point2i RegionToChunk()
        {
            return new Point2i(X << 5, Z << 5);
        }

        /// <summary>
        /// Get the local coordinates of a chunk within a region (0-31)
        /// Uses bitwise operation: localX = chunkX & 0x1F, localZ = chunkZ & 0x1F
        /// </summary>
        public Point2i GetLocalInRegion()
        {
            return new Point2i(X & 0x1F, Z & 0x1F);
        }

        /// <summary>
        /// Get the local coordinates of a chunk within a region (0-31) - alias method
        /// </summary>
        public Point2i GetLocalCoordinates()
        {
            return GetLocalInRegion();
        }

        /// <summary>
        /// Get the index of a chunk in the region file header array (0-1023)
        /// According to the official specification: index = localX + 32 * localZ
        /// </summary>
        public int GetRegionIndex()
        {
            var local = GetLocalInRegion();
            return local.X + local.Z * 32;
        }

        /// <summary>
        /// Convert local coordinates to chunk index (0-1023) - alias method
        /// </summary>
        public int ToChunkIndex()
        {
            return GetRegionIndex();
        }

        /// <summary>
        /// Convert region index to local coordinates
        /// </summary>
        public static Point2i FromRegionIndex(int index)
        {
            return new Point2i(index % 32, index / 32);
        }

        /// <summary>
        /// Create Point2i from chunk index - alias method
        /// </summary>
        public static Point2i FromChunkIndex(int index)
        {
            return FromRegionIndex(index);
        }

        /// <summary>
        /// Coordinate modulo operation, handles negatives
        /// </summary>
        public Point2i Mod(int modulus)
        {
            int modX = X % modulus;
            int modZ = Z % modulus;
            if (modX < 0) modX += modulus;
            if (modZ < 0) modZ += modulus;
            return new Point2i(modX, modZ);
        }

        /// <summary>
        /// Coordinate addition
        /// </summary>
        public Point2i Add(Point2i other)
        {
            return new Point2i(X + other.X, Z + other.Z);
        }

        /// <summary>
        /// Coordinate subtraction
        /// </summary>
        public Point2i Sub(Point2i other)
        {
            return new Point2i(X - other.X, Z - other.Z);
        }

        /// <summary>
        /// Convert to long for hash or key
        /// </summary>
        public long AsLong()
        {
            return ((long)X << 32) | (uint)Z;
        }

        /// <summary>
        /// Restore coordinates from long
        /// </summary>
        public static Point2i FromLong(long value)
        {
            return new Point2i((int)(value >> 32), (int)(value & 0xFFFFFFFF));
        }

        public override bool Equals(object? obj)
        {
            return obj is Point2i other && Equals(other);
        }

        public bool Equals(Point2i other)
        {
            return X == other.X && Z == other.Z;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Z);
        }

        public override string ToString()
        {
            return $"({X}, {Z})";
        }

        public static bool operator ==(Point2i left, Point2i right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Point2i left, Point2i right)
        {
            return !left.Equals(right);
        }

        public static Point2i operator +(Point2i left, Point2i right)
        {
            return left.Add(right);
        }

        public static Point2i operator -(Point2i left, Point2i right)
        {
            return left.Sub(right);
        }
    }
}
