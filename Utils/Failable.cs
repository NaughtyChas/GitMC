namespace GitMC.Utils
{
    public interface IFailable
    {
        string Description { get; }
        Exception? Exception { get; }
        bool IsSuccess { get; }
    }

    public interface IFailable<out T> : IFailable
    {
        T? Result { get; }
    }

    public class Failable : IFailable
    {
        public string Description { get; }
        public Exception? Exception { get; }
        public bool IsSuccess => Exception == null;

        public Failable(Exception exception, string description)
        {
            Exception = exception;
            Description = description;
        }
    }

    public class Failable<T> : IFailable<T>
    {
        public T? Result { get; }
        public string Description { get; }
        public Exception? Exception { get; }
        public bool IsSuccess => Exception == null;

        public Failable(T? result, Exception? exception, string description)
        {
            Result = result;
            Exception = exception;
            Description = description;
        }

        public Failable(Func<T> action, string description)
        {
            Description = description;
            try
            {
                Result = action();
                Exception = null;
            }
            catch (Exception ex)
            {
                Result = default(T);
                Exception = ex;
            }
        }
    }
}
