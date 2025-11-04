namespace Helios365.Core.Exceptions;

public class Helios365Exception : Exception
{
    public Helios365Exception() { }
    public Helios365Exception(string message) : base(message) { }
    public Helios365Exception(string message, Exception inner) : base(message, inner) { }
}

public class ValidationException : Helios365Exception
{
    public ValidationException() { }
    public ValidationException(string message) : base(message) { }
    public ValidationException(string message, Exception inner) : base(message, inner) { }
}

public class RepositoryException : Helios365Exception
{
    public RepositoryException() { }
    public RepositoryException(string message) : base(message) { }
    public RepositoryException(string message, Exception inner) : base(message, inner) { }
}

public class ResourceNotFoundException : RepositoryException
{
    public ResourceNotFoundException() { }
    public ResourceNotFoundException(string message) : base(message) { }
    public ResourceNotFoundException(string message, Exception inner) : base(message, inner) { }
}

public class ServiceException : Helios365Exception
{
    public ServiceException() { }
    public ServiceException(string message) : base(message) { }
    public ServiceException(string message, Exception inner) : base(message, inner) { }
}

public class ActionExecutionException : ServiceException
{
    public ActionExecutionException() { }
    public ActionExecutionException(string message) : base(message) { }
    public ActionExecutionException(string message, Exception inner) : base(message, inner) { }
}

public class EmailException : ServiceException
{
    public EmailException() { }
    public EmailException(string message) : base(message) { }
    public EmailException(string message, Exception inner) : base(message, inner) { }
}
