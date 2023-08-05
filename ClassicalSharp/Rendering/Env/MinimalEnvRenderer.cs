﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using ClassicalSharp.Events;
using System;

#if USE16_BIT
using BlockID = System.UInt16;
#else
using BlockID = System.Byte;
#endif

namespace ClassicalSharp.Renderers {
	/// <summary> Minimialistic environment renderer 
	/// - only sets the background/clear colour to blended fog colour.
	/// (no smooth fog, clouds, or proper overhead sky) </summary>
	public class MinimalEnvRenderer : EnvRenderer {
		
		public override void Init(Game game) {
			base.Init(game);
			gfx.Fog = false;
			gfx.ClearColour(map.Env.SkyCol);
		}
		
		public override void Render(double deltaTime) {
			if (map.IsNotLoaded) return;
			FastColour fogCol = FastColour.White;
			float fogDensity = 0;
			BlockID block = BlockOn(out fogDensity, out fogCol);
			gfx.ClearColour(fogCol);
			
			// TODO: rewrite this to avoid raising the event? want to avoid recreating vbos too many times often
			if (fogDensity != 0) {
				// Exp fog mode: f = e^(-density*coord)
				// Solve for f = 0.05 to figure out coord (good approx for fog end)
				float dist = (float)Math.Log(0.05) / -fogDensity;
				game.SetViewDistance(dist, false);
			} else {
				game.SetViewDistance(game.UserViewDistance, false);
			}
		}
		
		public override void OnNewMap(Game game) { }		
		public override void OnNewMapLoaded(Game game) { }		
		protected override void EnvVariableChanged(object sender, EnvVarEventArgs e) { }
	}
}
