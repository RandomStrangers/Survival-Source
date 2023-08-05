﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using System;
using System.Drawing;
using ClassicalSharp.Gui.Widgets;
using ClassicalSharp.Singleplayer;

namespace ClassicalSharp.Gui.Screens {
	public class MiscOptionsScreen : MenuOptionsScreen {
		
		public MiscOptionsScreen(Game game) : base(game) {
		}
		
		public override void Init() {
			base.Init();
			ContextRecreated();
			MakeValidators();
			MakeDescriptions();
		}
		
		protected override void ContextRecreated() {
			IServerConnection network = game.Server;
			widgets = new Widget[] {
				// Column 1
				!network.IsSinglePlayer ? null :
					MakeOpt(-1, -100, "Click distance", OnWidgetClick,
					     g => g.LocalPlayer.ReachDistance.ToString(),
					     (g, v) => g.LocalPlayer.ReachDistance = Utils.ParseDecimal(v)),
				
				MakeBool(-1, -50, "Music", OptionsKey.UseMusic,
				     OnWidgetClick, g => g.UseMusic,
				     (g, v) => { g.UseMusic = v; g.AudioPlayer.SetMusic(g.UseMusic); }),
				
				MakeBool(-1, 0, "Sound", OptionsKey.UseSound,
				     OnWidgetClick, g => g.UseSound,
				     (g, v) => { g.UseSound = v; g.AudioPlayer.SetSound(g.UseSound); }),
				
				MakeBool(-1, 50, "View bobbing", OptionsKey.ViewBobbing,
				         OnWidgetClick, g => g.ViewBobbing, (g, v) => g.ViewBobbing = v),
				
				// Column 2
				!network.IsSinglePlayer ? null :
					MakeBool(1, -100, "Block physics", OptionsKey.SingleplayerPhysics, OnWidgetClick,
					         g => ((SinglePlayerServer)network).physics.Enabled,
					         (g, v) => ((SinglePlayerServer)network).physics.Enabled = v),
				
				MakeBool(1, -50, "Auto close launcher", OptionsKey.AutoCloseLauncher, OnWidgetClick,
				         g => Options.GetBool(OptionsKey.AutoCloseLauncher, false),
				         (g, v) => Options.Set(OptionsKey.AutoCloseLauncher, v)),
				
				MakeBool(1, 0, "Invert mouse", OptionsKey.InvertMouse,
				         OnWidgetClick, g => g.InvertMouse, (g, v) => g.InvertMouse = v),
				
				MakeOpt(1, 50, "Mouse sensitivity", OnWidgetClick,
				     g => g.MouseSensitivity.ToString(),
				     (g, v) => { g.MouseSensitivity = Int32.Parse(v);
				     	Options.Set(OptionsKey.Sensitivity, v); }),
				
				MakeBack(false, titleFont,
				         (g, w) => g.Gui.SetNewScreen(new OptionsGroupScreen(g))),
				null, null,
			};
		}
		
		void MakeValidators() {
			IServerConnection network = game.Server;
			validators = new MenuInputValidator[] {
				network.IsSinglePlayer ? new RealValidator(1, 1024) : null,
				new BooleanValidator(),
				new BooleanValidator(),
				new BooleanValidator(),
				
				network.IsSinglePlayer ? new BooleanValidator() : null,
				new BooleanValidator(),
				new BooleanValidator(),
				new IntegerValidator(1, 200),
			};
		}
		
		void MakeDescriptions() {
			descriptions = new string[widgets.Length][];
			descriptions[0] = new string[] {
				"&eSets how far away you can place/delete blocks",
				"&fThe default click distance is 5 blocks.",
			};
		}
	}
}