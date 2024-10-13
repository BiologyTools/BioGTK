__global__ void copyTileToCanvas(unsigned char* canvas, int canvasWidth, int canvasHeight,
    unsigned char* tile, int tileWidth, int tileHeight,
    int offsetX, int offsetY) 
{
    // Calculate the global x and y index for the thread
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    // Calculate the source and destination indices
    int tileIdx = (y * tileWidth + x) * 3; // Each pixel has 3 components (RGB)
    int canvasIdx = ((y + offsetY) * canvasWidth + (x + offsetX)) * 3;
    // Copy the pixel (RGB components)
    canvas[canvasIdx] = tile[tileIdx];
    canvas[canvasIdx + 1] = tile[tileIdx + 1];
    canvas[canvasIdx + 2] = tile[tileIdx + 2];
}
