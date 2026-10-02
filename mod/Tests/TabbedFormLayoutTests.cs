using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TabbedFormLayoutTests
    {
        public static void Geometry()
        {
            foreach (float width in new[] { 0f, 3f, 132f, 700f })
                foreach (float height in new[] { 0f, 2f, 40f, 700f })
                {
                    var form = new UiLayoutRect(17f, 31f, width, height);
                    var layout = TabbedFormLayout.Arrange(form, 132f, 32f, 30f, 8f, 16f);
                    Contained(form, layout.Rail);
                    Contained(form, layout.Body);
                    Contained(form, layout.Footer);
                    if (layout.Rail.Width > 0f && layout.Body.Width > 0f)
                    {
                        AssertEx.True(layout.Body.X >= layout.Rail.XMax, "rail and body do not overlap");
                        AssertEx.Equal(16f, layout.Body.X - layout.Rail.XMax, "rail/body gap when both fit");
                    }
                    AssertEx.True(layout.Rail.YMax <= layout.Footer.Y || layout.Rail.Height == 0f,
                        "rail does not overlap footer");
                    AssertEx.True(layout.Body.YMax <= layout.Footer.Y || layout.Body.Height == 0f,
                        "body does not overlap footer");
                }
        }
        static void Contained(UiLayoutRect parent, UiLayoutRect child)
        {
            AssertEx.True(child.Width >= 0f && child.Height >= 0f &&
                child.X >= parent.X && child.Y >= parent.Y &&
                child.XMax <= parent.XMax && child.YMax <= parent.YMax,
                "tabbed geometry stays bounded");
        }

    }
}
