using System;

namespace SlopWorld
{
    // Single-field dialogs share submit admission and error settlement. Callers own field
    // values, validation, endpoint requests, and drawing through TextDialog.
    // Submit and its callbacks run on the UI/main thread; closing does not cancel the request.
    internal sealed class DialogSubmitState
    {
        public bool Pending { get; private set; }
        public string Error { get; private set; }

        public void Submit(
            string validationError,
            Action<Action, Action<string>> request,
            Action close
        )
        {
            if (Pending)
                return;
            Error = validationError;
            if (Error != null)
                return;

            // Set pending before dispatch, including requests that fail synchronously.
            // Success closes directly and leaves submission blocked for this dialog.
            Pending = true;
            request(close, Fail);
        }

        void Fail(string error)
        {
            Pending = false;
            Error = error;
        }
    }
}
