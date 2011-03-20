namespace Nancy.Hosting.Owin.Fakes
{
    using System;
    using System.Threading;
    using BodyDelegate = System.Func<System.Func<System.ArraySegment<byte>, // data
                             System.Action,                         // continuation
                             bool>,                                 // continuation will be invoked
                             System.Action<System.Exception>,       // onError
                             System.Action,                         // on Complete
                             System.Action>;                        // cancel

    public class FakeProducer
    {
        private Func<ArraySegment<byte>, Action, bool> onNext;
        private Action<Exception> onError;
        private Action onComplete;
        private bool active;

        private bool sendContinuation;
        private int currentIndex;
        private byte[] buffer;
        private int chunkSize;
        private bool autoSend;

        public FakeProducer(bool sendContinuation, byte[] buffer, int chunkSize, bool autoSend)
        {
            this.sendContinuation = sendContinuation;
            this.buffer = buffer;
            this.chunkSize = chunkSize;
            this.autoSend = autoSend;
        }

        public bool BodyDelegateInvoked { get; private set; }

        public bool IsComplete
        {
            get { return this.currentIndex >= this.buffer.Length; }
        }

        public Action BodyDelegate(Func<ArraySegment<byte>, Action, bool> onNext, Action<Exception> onError, Action onComplete)
        {
            this.onNext = onNext;
            this.onError = onError;
            this.onComplete = onComplete;

            this.BodyDelegateInvoked = true;

            if (this.autoSend)
            {
                ThreadPool.QueueUserWorkItem((s) => this.SendAll());
            }

            return this.OnCancel;
        }

        public void ThrowException(Exception e)
        {
            if (!this.BodyDelegateInvoked)
            {
                throw new InvalidOperationException("Body delegate not yet invoked");
            }

            this.onError.Invoke(e);
        }

        public void SendAll()
        {
            if (!this.BodyDelegateInvoked)
            {
                throw new InvalidOperationException("Body delegate not yet invoked");
            }

            while (!this.IsComplete)
            {
                this.SendChunk();
            }

            this.onComplete.Invoke();
        }

        public void SendChunk()
        {
            if (!this.BodyDelegateInvoked)
            {
                throw new InvalidOperationException("Body delegate not yet invoked");
            }

            if (this.IsComplete)
            {
                return;
            }

            var remainingBytes = this.buffer.Length - this.currentIndex;
            var currentChunkSize = Math.Min(remainingBytes, this.chunkSize);

            var currentChunk = new ArraySegment<byte>(this.buffer, this.currentIndex, currentChunkSize);

            this.currentIndex += currentChunkSize;

            if (this.sendContinuation)
            {
                // The continuation sets the reset event. If the consumer
                // returns false, signifying it won't call the continuation,
                // we set it straight away.
                var sync = new ManualResetEventSlim();
                if (!this.onNext(currentChunk, sync.Set))
                {
                    sync.Set();
                }

                // Wait for the contination to be called, if it is going to be
                sync.Wait();
            }
            else
            {
                if (this.onNext(currentChunk, null))
                {
                    throw new InvalidOperationException("Consumer returned true for 'will invoke continuation' when continuation was null");
                }
            }
        }

        public void InvokeOnComplete()
        {
            if (!this.BodyDelegateInvoked)
            {
                throw new InvalidOperationException("Body delegate not yet invoked");
            }

            this.onComplete.Invoke();
        }

        private void OnCancel()
        {
            this.Cancelled = true;

            this.active = false;
        }

        protected bool Cancelled { get; set; }

        public static implicit operator BodyDelegate(FakeProducer producer)
        {
            return producer.BodyDelegate;
        }
    }
}