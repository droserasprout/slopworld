using System;
using UnityEngine;

namespace SlopWorld
{
    // Callbacks run on the UI thread. Field owners decide whether a delayed read is still
    // applicable; providers own host access and fallback behavior.
    public interface IUiClipboard
    {
        void Copy(string text, bool primary);
        void Read(bool primary, Action<string> receive);
    }

    public static class UiClipboard
    {
        static IUiClipboard _provider = new NativeClipboard();

        public static IUiClipboard Provider
        {
            get => _provider;
            set => _provider = value ?? throw new ArgumentNullException(nameof(value));
        }

        sealed class NativeClipboard : IUiClipboard
        {
            public void Copy(string text, bool primary)
            {
                if (!primary && !string.IsNullOrEmpty(text)) GUIUtility.systemCopyBuffer = text;
            }

            public void Read(bool primary, Action<string> receive)
            {
                if (!primary) receive(GUIUtility.systemCopyBuffer);
            }
        }
    }
}
