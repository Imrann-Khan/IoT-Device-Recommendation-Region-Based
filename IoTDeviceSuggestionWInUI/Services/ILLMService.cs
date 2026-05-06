// LLM services removed - using pure ONNX bi-encoder + cross-encoder pipeline only
using System;

namespace IoTDeviceSuggestionWInUI.Services
{
    public interface ILLMService : IDisposable
    {
        void Dispose();
    }
}
