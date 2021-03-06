﻿namespace Myra.Graphics2D.UI.Styles
{
	public class ListBoxStyle: WidgetStyle
	{
		public ImageTextButtonStyle ListItemStyle { get; set; }
		public SeparatorStyle SeparatorStyle { get; set; }

		public ListBoxStyle()
		{
		}

		public ListBoxStyle(ListBoxStyle style) : base(style)
		{
			ListItemStyle = style.ListItemStyle != null ? new ImageTextButtonStyle(style.ListItemStyle) : null;
			SeparatorStyle = style.SeparatorStyle != null ? new SeparatorStyle(style.SeparatorStyle) : null;
		}

		public override WidgetStyle Clone()
		{
			return new ListBoxStyle(this);
		}
	}
}