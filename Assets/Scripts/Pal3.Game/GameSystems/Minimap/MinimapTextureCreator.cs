// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2023, Jiaqi Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Minimap
{
    using System.Buffers;
    using Core.DataReader.Nav;
    using Engine.Core.Abstraction;

    using Color32 = UnityEngine.Color32;

    public sealed class MinimapTextureCreator
    {
        private readonly ITextureFactory _textureFactory;

        private readonly Color32 _obstacleColor;
        private readonly Color32 _wallColor;
        private readonly Color32 _floorColor;

        public MinimapTextureCreator(ITextureFactory textureFactory,
            Color32 obstacleColor,
            Color32 wallColor,
            Color32 floorColor)
        {
            _textureFactory = textureFactory;
            _obstacleColor = obstacleColor;
            _wallColor = wallColor;
            _floorColor = floorColor;
        }

        /// <summary>
        /// Creates a Texture2D for the given NavTileLayer, representing a minimap of the layer.
        /// </summary>
        /// <param name="layer">The NavTileLayer to create the minimap for.</param>
        /// <returns>A Texture2D representing the minimap of the NavTileLayer.</returns>
        public ITexture2D CreateMinimapTexture(NavTileLayer layer)
        {
            byte[] rgbaData = ArrayPool<byte>.Shared.Rent(layer.Width * layer.Height * 4);

            for (var i = 0; i < layer.Width; i++)
            {
                for (int j = 0; j < layer.Height; j++)
                {
                    NavTile tile = layer.Tiles[i + j * layer.Width];

                    Color32 color = tile.DistanceToNearestObstacle switch
                    {
                        0 => _obstacleColor,
                        1 => _wallColor,
                        _ => _floorColor
                    };

                    // NOTE: the texture is flipped vertically compared to the tilemap space
                    int colorIndex = (i + (layer.Height - j - 1) * layer.Width) * 4;
                    rgbaData[colorIndex + 0] = color.r;
                    rgbaData[colorIndex + 1] = color.g;
                    rgbaData[colorIndex + 2] = color.b;
                    rgbaData[colorIndex + 3] = color.a;
                }
            }

            try
            {
                return _textureFactory.CreateTexture(layer.Width, layer.Height, rgbaData);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rgbaData);
            }
        }
    }
}