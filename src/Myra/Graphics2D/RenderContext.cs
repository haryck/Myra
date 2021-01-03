// FillRectangle/DrawRectangle code had been borrowed from the MonoGame.Extended project: https://github.com/craftworkgames/MonoGame.Extended

using FontStashSharp;
using System;
using Myra.Utility;
using System.Text;

#if MONOGAME || FNA
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#elif STRIDE
using Stride.Core.Mathematics;
using Stride.Graphics;
using Texture2D = Stride.Graphics.Texture;
#else
using System.Drawing;
using Myra.Platform;
using Vector2 = System.Drawing.PointF;
using Texture2D = System.Object;
#endif

namespace Myra.Graphics2D
{
	public partial class RenderContext
	{
#if MONOGAME || FNA
		private static RasterizerState _uiRasterizerState;

		private static RasterizerState UIRasterizerState
		{
			get
			{
				if (_uiRasterizerState != null)
				{
					return _uiRasterizerState;
				}

				_uiRasterizerState = new RasterizerState
				{
					ScissorTestEnable = true
				};
				return _uiRasterizerState;
			}
		}
#elif STRIDE
		private static readonly RasterizerStateDescription _uiRasterizerState;

		static RenderContext()
		{
			var rs = new RasterizerStateDescription();
			rs.SetDefault();
			rs.ScissorTestEnable = true;
			_uiRasterizerState = rs;
		}
#endif

#if MONOGAME || FNA || STRIDE
		private readonly SpriteBatch _renderer;
#else
		private readonly IMyraRenderer _renderer;
#endif
		private bool _beginCalled;
		private Matrix? _transform;

		public Matrix? Transform
		{
			get
			{
				return _transform;
			}

			set
			{
				if (value == _transform)
				{
					return;
				}

				_transform = value;

				if (_transform != null)
				{
					InverseTransform = Matrix.Invert(_transform.Value);
				}
			}
		}

		internal Matrix InverseTransform { get; set; }

		public Rectangle Scissor
		{
			get
			{
#if MONOGAME || FNA
				var device = _renderer.GraphicsDevice;
				var rect = device.ScissorRectangle;

				rect.X -= device.Viewport.X;
				rect.Y -= device.Viewport.Y;

				return rect;
#elif STRIDE
				return MyraEnvironment.Game.GraphicsContext.CommandList.Scissor;
#else
				return _renderer.Scissor;
#endif
			}

			set
			{

				if (Transform != null)
				{
					var pos = new Vector2(value.X, value.Y).Transform(Transform.Value);
					var size = new Vector2(value.Width, value.Height).Transform(Transform.Value);

					value = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
				}

#if MONOGAME || FNA
				Flush();
				var device = _renderer.GraphicsDevice;
				value.X += device.Viewport.X;
				value.Y += device.Viewport.Y;
				device.ScissorRectangle = value;
#elif STRIDE
				Flush();
				MyraEnvironment.Game.GraphicsContext.CommandList.SetScissorRectangle(value);
#else
				_renderer.Scissor = value;
#endif
			}
		}

		public Rectangle View { get; set; }

		public float Opacity { get; set; }

		public RenderContext()
		{
#if MONOGAME || FNA || STRIDE
			_renderer = new SpriteBatch(MyraEnvironment.Game.GraphicsDevice);
#else
			_renderer = MyraEnvironment.Platform.CreateRenderer();
#endif
		}

		public void Draw(Texture2D texture, Rectangle destinationRectangle, Color color)
		{
			_renderer.Draw(texture, destinationRectangle, color);
		}

		public void Draw(Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color)
		{
#if MONOGAME || FNA
			_renderer.Draw(texture, destinationRectangle, sourceRectangle, color);
#elif STRIDE
			_renderer.Draw(texture, destinationRectangle, sourceRectangle, color, 0, Mathematics.Vector2Zero);
#else
			_renderer.Draw(texture, destinationRectangle, sourceRectangle, color);
#endif
		}

		public void Draw(Texture2D texture, Vector2 position, Color color)
		{
			_renderer.Draw(texture, position, color);
		}
		
		public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color)
		{
			_renderer.Draw(texture, position, sourceRectangle, color);
		}

#if MONOGAME || FNA || STRIDE
		public void Draw(Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
		{
#if MONOGAME || FNA
			_renderer.Draw(texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
#else
			_renderer.Draw(texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, ImageOrientation.AsIs, layerDepth);
#endif
		}

		public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
		{
#if MONOGAME || FNA
			_renderer.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
#else
			_renderer.Draw(texture, position, sourceRectangle, color, rotation, origin, scale, effects, ImageOrientation.AsIs, layerDepth);
#endif
		}
#endif

		public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color)
		{
			_renderer.DrawString(font, text, position, color);
		}

		public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, Vector2 origin, Vector2 scale, float layerDepth)
		{
			_renderer.DrawString(font, text, position, color, scale, origin, layerDepth);
		}

		public void DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color color, Vector2 scale, float layerDepth)
		{
			_renderer.DrawString(font, text, position, color, scale, layerDepth);
		}

		public void DrawString(SpriteFontBase font, StringBuilder text, Vector2 position, Color color)
		{
			_renderer.DrawString(font, text, position, color);
		}

		internal void Begin()
		{
#if MONOGAME || FNA
			_renderer.Begin(SpriteSortMode.Deferred,
				BlendState.AlphaBlend,
				SamplerState.PointClamp,
				null,
				UIRasterizerState);
#elif STRIDE
			_renderer.Begin(MyraEnvironment.Game.GraphicsContext,
				SpriteSortMode.Deferred,
				BlendStates.AlphaBlend,
				MyraEnvironment.Game.GraphicsDevice.SamplerStates.PointClamp,
				DepthStencilStates.Default,
				_uiRasterizerState);
#else
			_renderer.Begin();
#endif

			_beginCalled = true;
		}

		internal void End()
		{
			_renderer.End();
			_beginCalled = false;
		}

		internal void Flush()
		{
			if (!_beginCalled)
			{
				return;
			}

			End();
			Begin();
		}
	}
}