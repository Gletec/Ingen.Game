﻿using Ingen.Game.Framework.Resources;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Unity;
using Unity.Lifetime;
using DW = SharpDX.DirectWrite;
using WIC = SharpDX.WIC;

namespace Ingen.Game.Framework
{
	public class GameContainer : IDisposable
	{
		public UnityContainer Container { get; }

		public GameForm GameWindow { get; }

		#region DW/WIC
		WIC.ImagingFactory _imagingFactory;
		public ref WIC.ImagingFactory ImagingFactory => ref _imagingFactory;
		DW.Factory _dWFactory;
		public ref DW.Factory DWFactory => ref _dWFactory;
		#endregion

		public Scene CurrentScene { get; private set; }

		public bool IsLinkFrameAndLogic { get; }

		private HighPerformanceStopwatch Stopwatch { get; }
		public TimeSpan Elapsed => Stopwatch.Elapsed;

		public ResourceLoader GlobalResource { get; }

		public int WindowWidth { get; set; }
		public int WindowHeight { get; set; }

		private CancellationTokenSource TasksCancellationTokenSource;
		private Task RenderTask;
		private Task LogicTask;

		public ushort TpsRate { get; set; } = 30;

		private List<Overlay> Overlays { get; } = new List<Overlay>();
		public void AddOverlay(Overlay overlay)
		{
			if (GameWindow?.RenderTarget != null)
				overlay.UpdateRenderTarget(GameWindow.RenderTarget);
			for (var i = 0; i < Overlays.Count; i++)
				if (overlay.Priority <= Overlays[i].Priority)
				{
					Overlays.Insert(i, overlay);
					return;
				}
			Overlays.Add(overlay);
		}

		public GameContainer(bool isLinkFrameAndLogic, string windowTitle, int windowWidth, int windowHeight)
		{
			IsLinkFrameAndLogic = isLinkFrameAndLogic;

			WindowWidth = windowWidth;
			WindowHeight = windowHeight;

			Container = new UnityContainer();
			AddSingleton(this);
			AddSingleton(GameWindow = new GameForm() { ClientSize = new Size(windowWidth, windowHeight), Text = windowTitle });
			AddSingleton(DWFactory = new DW.Factory());
			AddSingleton(ImagingFactory = new WIC.ImagingFactory());
			AddSingleton(Stopwatch = new HighPerformanceStopwatch());
			AddSingleton(GlobalResource = new ResourceLoader());

			TasksCancellationTokenSource = new CancellationTokenSource();
			RenderTask = new Task(Render, TasksCancellationTokenSource.Token, TaskCreationOptions.LongRunning);
			if (!IsLinkFrameAndLogic)
				LogicTask = new Task(Logic, TasksCancellationTokenSource.Token, TaskCreationOptions.LongRunning);
		}

		public T Resolve<T>()
			=> Container.BuildUp(Container.Resolve<T>());
		public void AddSingleton<T>(T instance = null) where T : class
		{
			if (instance == null)
				Container.RegisterSingleton<T>();
			else
				Container.RegisterInstance(instance, new ContainerControlledLifetimeManager());
		}

		public void Navigate<TScene>(TransitionScene loadingScene) where TScene : Scene
			=> Navigate(loadingScene, Container.Resolve<TScene>());
		public void Navigate(TransitionScene loadingScene, Scene nextScene)
		{
			if (CurrentScene is TransitionScene)
				throw new InvalidOperationException("TransitionSceneからNavigateすることはできません。");

			loadingScene.Initalize(CurrentScene, nextScene);
			CurrentScene = loadingScene;
		}

		public void Start(Scene startupScene)
		{
			CurrentScene = startupScene;
			GameWindow.Initalize();

			GlobalResource.UpdateRenderTarget(GameWindow.RenderTarget);
			CurrentScene.UpdateRenderTarget(GameWindow.RenderTarget);

			Overlays.ForEach(o => o.UpdateRenderTarget(GameWindow.RenderTarget));

			Stopwatch.Start();
			RenderTask.Start();

			beforeLogicTime = Stopwatch.Elapsed;
			LogicTask?.Start();

			GameWindow.ShowDialog();

			TasksCancellationTokenSource.Cancel();
			LogicTask?.Wait();
			RenderTask.Wait();
			Stopwatch.Stop();
		}

		TimeSpan beforeRenderTime;
		void Render()
		{
			while (!TasksCancellationTokenSource.Token.IsCancellationRequested)
			{
				var wait = (1000.0 / 60) - (Stopwatch.Elapsed - beforeRenderTime).TotalMilliseconds;
				if (wait >= 1)
					Thread.Sleep((int)wait);
				beforeRenderTime = Stopwatch.Elapsed;

				if (IsLinkFrameAndLogic)
				{
					CurrentScene.Update();
					Overlays.ForEach(o => o.Update());
				}
				if (GameWindow.WindowState == System.Windows.Forms.FormWindowState.Minimized)
					continue;
				GameWindow.BeginDraw();
				CurrentScene.Render();
				Overlays.ForEach(o => o.Render());
				GameWindow.EndDraw();
			}
		}

		TimeSpan beforeLogicTime;
		void Logic()
		{
			while (!TasksCancellationTokenSource.Token.IsCancellationRequested)
			{
				var wait = (1000.0 / TpsRate) - (Stopwatch.Elapsed - beforeLogicTime).TotalMilliseconds;
				if (wait >= 1)
					Thread.Sleep((int)wait);
				beforeLogicTime = Stopwatch.Elapsed;

				CurrentScene.Update();
				Overlays.ForEach(o => o.Update());
			}
		}

		private bool isDisposed = false;
		public void Dispose()
		{
			if (isDisposed)
				return;
			isDisposed = true;
			lock (Overlays)
				Overlays.ForEach(o => o.Dispose());
			GameWindow?.Dispose();
			Container?.Dispose();
		}
	}
}