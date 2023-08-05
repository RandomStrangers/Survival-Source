﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using System;
using ClassicalSharp.Entities;
using ClassicalSharp.Gui.Screens;
using ClassicalSharp.Hotkeys;
using OpenTK;
using OpenTK.Input;

namespace ClassicalSharp {

	public sealed class InputHandler {
		
		#if !ANDROID
		public HotkeyList Hotkeys;
		#endif
		
		Game game;
		bool[] buttonsDown = new bool[3];
		PickingHandler picking;
		public InputHandler(Game game) {
			this.game = game;
			RegisterInputHandlers();
			Keys = new KeyMap();
			picking = new PickingHandler(game, this);
			
			#if !ANDROID
			Hotkeys = new HotkeyList();
			Hotkeys.LoadSavedHotkeys();
			#endif
		}
		
		void RegisterInputHandlers() {
			game.Keyboard.KeyDown += KeyDownHandler;
			game.Keyboard.KeyUp += KeyUpHandler;
			game.window.KeyPress += KeyPressHandler;
			game.Mouse.WheelChanged += MouseWheelChanged;
			game.Mouse.Move += MouseMove;
			game.Mouse.ButtonDown += MouseButtonDown;
			game.Mouse.ButtonUp += MouseButtonUp;
		}
		
		public bool AltDown { get { return IsKeyDown(Key.AltLeft) || IsKeyDown(Key.AltRight); } }
		public bool ControlDown { get { return IsKeyDown(Key.ControlLeft) || IsKeyDown(Key.ControlRight); } }
		public bool ShiftDown { get { return IsKeyDown(Key.ShiftLeft) || IsKeyDown(Key.ShiftRight); } }
		
		public KeyMap Keys;
		public bool IsKeyDown(Key key) {
			return game.Keyboard[key];
		}
		
		/// <summary> Returns whether the key associated with the given key binding is currently held down. </summary>
		public bool IsKeyDown(KeyBind binding) {
			Key key = Keys[binding];
			return game.Keyboard[key];
		}
		
		public bool IsMousePressed(MouseButton button) {
			bool down = game.Mouse[button];
			if (down) return true;
			
			// Key --> mouse mappings
			if (button == MouseButton.Left && IsKeyDown(KeyBind.MouseLeft)) return true;
			if (button == MouseButton.Middle && IsKeyDown(KeyBind.MouseMiddle)) return true;
			if (button == MouseButton.Right && IsKeyDown(KeyBind.MouseRight)) return true;
			return false;
		}
		
		public void PickBlocks(bool cooldown, bool left, bool middle, bool right) {
			picking.PickBlocks(cooldown, left, middle, right);
		}
		
		// defer getting the targeted entity as it's a costly operation
		internal int pickingId = -1;
		internal void ButtonStateChanged(MouseButton button, bool pressed) {
			if (buttonsDown[(int)button]) {
				if (pickingId == -1) {
					pickingId = game.Entities.GetClosetPlayer(game.LocalPlayer);
				}
				
				game.Server.SendPlayerClick(button, false, (byte)pickingId, game.SelectedPos);
				buttonsDown[(int)button] = false;
			}
			
			if (pressed) {
				if (pickingId == -1) {
					pickingId = game.Entities.GetClosetPlayer(game.LocalPlayer);
				}
				
				game.Server.SendPlayerClick(button, true, (byte)pickingId, game.SelectedPos);
				buttonsDown[(int)button] = true;
			}
		}
		
		internal void ScreenChanged(Screen oldScreen, Screen newScreen) {
			if (oldScreen != null && oldScreen.HandlesAllInput)
				picking.lastClick = DateTime.UtcNow;
			
			if (game.Server.UsingPlayerClick) {
				pickingId = -1;
				ButtonStateChanged(MouseButton.Left, false);
				ButtonStateChanged(MouseButton.Right, false);
				ButtonStateChanged(MouseButton.Middle, false);
			}
		}
		
		
		#region Event handlers
		
		void MouseButtonUp(object sender, MouseButtonEventArgs e) {
			if (!game.Gui.ActiveScreen.HandlesMouseUp(e.X, e.Y, e.Button)) {
				if (game.Server.UsingPlayerClick && e.Button <= MouseButton.Middle) {
					pickingId = -1;
					ButtonStateChanged(e.Button, false);
				}
			}
		}

		void MouseButtonDown(object sender, MouseButtonEventArgs e) {
			if (!game.Gui.ActiveScreen.HandlesMouseClick(e.X, e.Y, e.Button)) {
				bool left = e.Button == MouseButton.Left;
				bool middle = e.Button == MouseButton.Middle;
				bool right = e.Button == MouseButton.Right;
				PickBlocks(false, left, middle, right);
			} else {
				picking.lastClick = DateTime.UtcNow;
			}
		}

		void MouseMove(object sender, MouseMoveEventArgs e) {
			if (!game.Gui.ActiveScreen.HandlesMouseMove(e.X, e.Y)) {
			}
		}

		float deltaAcc = 0;
		void MouseWheelChanged(object sender, MouseWheelEventArgs e) {
			if (game.Gui.ActiveScreen.HandlesMouseScroll(e.Delta)) return;
			
			Inventory inv = game.Inventory;
			bool hotbar = AltDown || ControlDown || ShiftDown;
			if ((!hotbar && game.Camera.Zoom(e.DeltaPrecise)) || DoFovZoom(e.DeltaPrecise) || !inv.CanChangeHeldBlock)
				return;
			ScrollHotbar(e.DeltaPrecise);
		}
		
		void ScrollHotbar(float deltaPrecise) {
			Inventory inv = game.Inventory;
			if (AltDown) {
				int index = inv.Offset / Inventory.BlocksPerRow;
				inv.Offset = ScrolledIndex(deltaPrecise, index) * Inventory.BlocksPerRow;
			} else {
				inv.SelectedIndex = ScrolledIndex(deltaPrecise, inv.SelectedIndex);
			}
		}
		
		int ScrolledIndex(float deltaPrecise, int currentIndex) {
			// Some mice may use deltas of say (0.2, 0.2, 0.2, 0.2, 0.2)
			// We must use rounding at final step, not at every intermediate step.
			deltaAcc += deltaPrecise;
			int delta = (int)deltaAcc;
			deltaAcc -= delta;
			
			const int blocksPerRow = Inventory.BlocksPerRow;
			int diff = -delta % blocksPerRow;
			int index = currentIndex + diff;
			
			if (index < 0) index += blocksPerRow;
			if (index >= blocksPerRow) index -= blocksPerRow;
			return index;
		}

		void KeyPressHandler(object sender, KeyPressEventArgs e) {
			char key = e.KeyChar;
			if (!game.Gui.ActiveScreen.HandlesKeyPress(key)) {
			}
		}
		
		void KeyUpHandler(object sender, KeyboardKeyEventArgs e) {
			Key key = e.Key;
			if (SimulateMouse(key, false)) return;
			
			if (!game.Gui.ActiveScreen.HandlesKeyUp(key)) {
				if (key == Keys[KeyBind.ZoomScrolling])
					SetFOV(game.DefaultFov, false);
			}
		}

		static int[] viewDistances = { 16, 32, 64, 128, 256, 512, 1024, 2048, 4096 };
		Key lastKey;
		void KeyDownHandler(object sender, KeyboardKeyEventArgs e) {
			Key key = e.Key;
			if (SimulateMouse(key, true)) return;
			
			
			if (IsShutdown(key)) {
				game.Exit();
			} else if (key == Keys[KeyBind.Screenshot]) {
				game.screenshotRequested = true;
			} else if (!game.Gui.ActiveScreen.HandlesKeyDown(key)) {
				if (!HandleBuiltinKey(key) && !game.LocalPlayer.input.Handles(key))
					HandleHotkey(key);
			}
			lastKey = key;
		}
		
		bool IsShutdown(Key key) {
			if (key == Key.F4 && (lastKey == Key.AltLeft || lastKey == Key.AltRight))
				return true;
			// On OSX, Cmd+Q should also terminate the process.
			if (!OpenTK.Configuration.RunningOnMacOS) return false;
			return key == Key.Q && (lastKey == Key.WinLeft || lastKey == Key.WinRight);
		}
		
		void HandleHotkey(Key key) {
			string text;
			bool more;
			if (!Hotkeys.IsHotkey(key, game.Input, out text, out more)) return;
			
			if (!more) {
				game.Server.SendChat(text, false);
			} else if (game.Gui.activeScreen == null) {
				game.Gui.hudScreen.OpenTextInputBar(text);
			}
		}
		
		MouseButtonEventArgs simArgs = new MouseButtonEventArgs();
		bool SimulateMouse(Key key, bool pressed) {
			Key left = Keys[KeyBind.MouseLeft], middle = Keys[KeyBind.MouseMiddle],
			right = Keys[KeyBind.MouseRight];
			
			if (!(key == left || key == middle || key == right))
				return false;
			simArgs.Button = key == left ? MouseButton.Left :
				key == middle ? MouseButton.Middle : MouseButton.Right;
			simArgs.X = game.Mouse.X;
			simArgs.Y = game.Mouse.Y;
			simArgs.IsPressed = pressed;
			
			if (pressed) MouseButtonDown(null, simArgs);
			else MouseButtonUp(null, simArgs);
			return true;
		}
		
		bool HandleBuiltinKey(Key key) {
			if (key == Keys[KeyBind.HideGui]) {
				game.HideGui = !game.HideGui;
			} else if (key == Keys[KeyBind.HideFps]) {
				game.ShowFPS = !game.ShowFPS;
			} else if (key == Keys[KeyBind.Fullscreen]) {
				WindowState state = game.window.WindowState;
				if (state != WindowState.Minimized) {
					game.window.WindowState = state == WindowState.Fullscreen ?
						WindowState.Normal : WindowState.Fullscreen;
				}
			} else if (key == Keys[KeyBind.AxisLines]) {
				ToggleAxisLines();
			} else if (key == Keys[KeyBind.Autorotate]) {
				ToggleAutoRotate();
			} else if (key == Keys[KeyBind.ThirdPerson]) {
				game.CycleCamera();
			} else if (key == Keys[KeyBind.ToggleFog]) {
				if (game.Input.ShiftDown) {
					CycleDistanceBackwards();
				} else {
					CycleDistanceForwards();
				}
			} else if (key == Keys[KeyBind.PauseOrExit] && !game.World.IsNotLoaded) {
				game.Gui.SetNewScreen(new PauseScreen(game));
			} else if (!game.Mode.HandlesKeyDown(key)) {
				return false;
			}
			return true;
		}
		
		void ToggleAxisLines() {
			game.ShowAxisLines = !game.ShowAxisLines;
			Key key = Keys[KeyBind.AxisLines];
			if (game.ShowAxisLines) {
				game.Chat.Add("  &eAxis lines (&4X&e, &2Y&e, &1Z&e) now show. Press &a" + key + " &eto disable.");
			} else {
				game.Chat.Add("  &eAxis lines no longer show. Press &a" + key + " &eto re-enable.");
			}
		}
		
		void ToggleAutoRotate() {
			game.autoRotate = !game.autoRotate;
			Key key = Keys[KeyBind.Autorotate];
			if (game.autoRotate) {
				game.Chat.Add("  &eAuto rotate is &aenabled. &ePress &a" + key + " &eto disable.");
			} else {
				game.Chat.Add("  &eAuto rotate is &cdisabled. &ePress &a" + key + " &eto re-enable.");
			}
		}
		
		void CycleDistanceForwards() {
			for (int i = 0; i < viewDistances.Length; i++) {
				int dist = viewDistances[i];
				if (dist > game.UserViewDistance) {
					game.SetViewDistance(dist, true); return;
				}
			}
			game.SetViewDistance(viewDistances[0], true);
		}
		
		void CycleDistanceBackwards() {
			for (int i = viewDistances.Length - 1; i >= 0; i--) {
				int dist = viewDistances[i];
				if (dist < game.UserViewDistance) {
					game.SetViewDistance(dist, true); return;
				}
			}
			game.SetViewDistance(viewDistances[viewDistances.Length - 1], true);
		}
		
		float fovIndex = -1;
		bool DoFovZoom(float deltaPrecise) {
			if (!game.IsKeyDown(KeyBind.ZoomScrolling)) return false;
			LocalPlayer p = game.LocalPlayer;
			if (!p.Hacks.Enabled || !p.Hacks.CanAnyHacks || !p.Hacks.CanUseThirdPersonCamera)
				return false;
			
			if (fovIndex == -1) fovIndex = game.ZoomFov;
			fovIndex -= deltaPrecise * 5;
			
			Utils.Clamp(ref fovIndex, 1, game.DefaultFov);
			return SetFOV((int)fovIndex, true);
		}
		
		public bool SetFOV(int fov, bool setZoom) {
			LocalPlayer p = game.LocalPlayer;
			if (game.Fov == fov) return true;
			if (!p.Hacks.Enabled || !p.Hacks.CanAnyHacks || !p.Hacks.CanUseThirdPersonCamera)
				return false;
			
			game.Fov = fov;
			if (setZoom) game.ZoomFov = fov;
			game.UpdateProjection();
			return true;
		}
		#endregion
	}
}