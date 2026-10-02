using System;

namespace SlopWorld
{
    // Drawing measures IMGUI content. Publish the result on the next frame so input
    // and repaint within a frame see the same scroll extent.
    public sealed class ContentHeight
    {
        int _frame = -1;
        float _height, _pending;

        public ContentHeight(float estimate = 900f)
        {
            _height = _pending = Math.Max(0f, estimate);
        }

        public float BeginFrame(int frame)
        {
            if (_frame != frame)
            {
                _height = _pending;
                _frame = frame;
            }
            return _height;
        }

        public void Measure(float height) => _pending = Math.Max(0f, height);
    }
}
