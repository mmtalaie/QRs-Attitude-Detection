using System;
using System.Drawing;


namespace QRs
{
    struct GlyphVisualizationData
    {
        // Color to use for highlight and glyph name
        public Color Color;
        // Image to show in the quadrilateral of recognized glyph
        public string ImageName;
        // 3D model name to show for the glyph
        public string ModelName;

        public GlyphVisualizationData(Color color)
        {
            Color = color;
            ImageName = null;
            ModelName = null;
        }
    }
}
