﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using System;
using System.Drawing;
using ClassicalSharp.GraphicsAPI;
#if ANDROID
using Android.Graphics;
#endif

namespace ClassicalSharp {
	
	/// <summary> Represents a 2D packed texture atlas, specifically for terrain.png. </summary>
	public class TerrainAtlas2D : IDisposable {
		
		public const int ElementsPerRow = 16, RowsCount = 16;
		public const float invElementSize = 1 / 16f;
		public Bitmap AtlasBitmap;
		public int elementSize;
		IGraphicsApi gfx;
		IDrawer2D drawer;
		
		public TerrainAtlas2D(IGraphicsApi gfx, IDrawer2D drawer) {
			this.gfx = gfx;
			this.drawer = drawer;
		}
		
		/// <summary> Updates the underlying atlas bitmap, fields, and texture. </summary>
		public void UpdateState(BlockInfo info, Bitmap bmp) {
			AtlasBitmap = bmp;
			elementSize = bmp.Width / ElementsPerRow;
			using (FastBitmap fastBmp = new FastBitmap(bmp, true, true))
				info.RecalculateSpriteBB(fastBmp);
		}
		
		/// <summary> Creates a new texture that contains the tile at the specified index. </summary>
		public int LoadTextureElement(int index) {
			int size = elementSize;
			using (FastBitmap atlas = new FastBitmap(AtlasBitmap, true, true))
				using (Bitmap bmp = Platform.CreateBmp(size, size))
					using (FastBitmap dst = new FastBitmap(bmp, true, false))
			{
				int x = index % ElementsPerRow, y = index / ElementsPerRow;
				FastBitmap.MovePortion(x * size, y * size, 0, 0, atlas, dst, size);
				return gfx.CreateTexture(dst, true);
			}
		}
		
		/// <summary> Disposes of the underlying atlas bitmap and texture. </summary>
		public void Dispose() {
			if (AtlasBitmap != null)
				AtlasBitmap.Dispose();
		}
	}
}