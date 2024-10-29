__global__ void copyTileToCanvas(
    unsigned char* canvas, int canvasWidth, int canvasHeight,
    unsigned char* tile, int tileWidth, int tileHeight,
    int offsetX, int offsetY,
    int canvasTileWidth, int canvasTileHeight)
{
    // Calculate the global x and y index for the thread
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    // Ensure this thread only processes pixels within the bounds of the tile and canvas section
    if (x < canvasTileWidth && y < canvasTileHeight) {
        int canvasX = x + offsetX;
        int canvasY = y + offsetY;

        // Ensure canvas indices are within bounds of the canvas extent
        if (canvasX < canvasWidth && canvasY < canvasHeight) {
            // Calculate scaling factors to map canvasTileWidth and canvasTileHeight to tile dimensions
            float scaleX = static_cast<float>(tileWidth) / static_cast<float>(canvasTileWidth);
            float scaleY = static_cast<float>(tileHeight) / static_cast<float>(canvasTileHeight);

            // Compute the corresponding tile indices
            int tileX = min(static_cast<int>(x * scaleX), tileWidth - 1);
            int tileY = min(static_cast<int>(y * scaleY), tileHeight - 1);

            // Calculate the indices for both canvas and tile in the 1D arrays
            int tileIdx = (tileY * tileWidth + tileX) * 3;   // Each pixel has 3 components (RGB)
            int canvasIdx = (canvasY * canvasWidth + canvasX) * 3;

            // Copy the pixel (RGB components) from the tile to the canvas
            canvas[canvasIdx] = tile[tileIdx + 2];        // Blue component
            canvas[canvasIdx + 1] = tile[tileIdx + 1];    // Green component
            canvas[canvasIdx + 2] = tile[tileIdx];        // Red component
        }
    }
}
