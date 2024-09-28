// Struct to define the extent of a tile
struct Extent {
    double MinX;
    double MinY;
    double MaxX;
    double MaxY;
};

// Struct to hold tile data
struct TileData {
    Extent Extent;              // The extent of the tile
    unsigned char* DevTilePtr;  // Pointer to the tile data on the GPU
};

__global__ void StitchKernel(
    unsigned char* devCanvas,
    int canvasWidth,
    int canvasHeight,
    TileData* devTiles,
    int tileCount,
    double minX,
    double minY)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x < canvasWidth && y < canvasHeight)
    {
        for (int i = 0; i < tileCount; ++i)
        {
            Extent extent = devTiles[i].Extent;
            unsigned char* tileData = (unsigned char*)devTiles[i].DevTilePtr;

            int startX = (int)(extent.MinX - minX);
            int startY = (int)(extent.MinY - minY);
            int tileWidth = (int)(extent.MaxX - extent.MinX);
            int tileHeight = (int)(extent.MaxY - extent.MinY);

            if (x >= startX && x < startX + tileWidth && y >= startY && y < startY + tileHeight)
            {
                int canvasIndex = (y * canvasWidth + x) * 3;
                int tileIndex = ((y - startY) * tileWidth + (x - startX)) * 3;

                // Avoid race conditions by ensuring only one tile writes to each pixel.
                devCanvas[canvasIndex] = tileData[tileIndex];
                devCanvas[canvasIndex + 1] = tileData[tileIndex + 1];
                devCanvas[canvasIndex + 2] = tileData[tileIndex + 2];
            }
        }
    }
}