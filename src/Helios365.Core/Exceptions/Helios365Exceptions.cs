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

public class HealthCheckException : ServiceException
{
    public HealthCheckException() { }
    public HealthCheckException(string message) : base(message) { }
    public HealthCheckException(string message, Exception inner) : base(message, inner) { }
}

public class RemediationException : ServiceException
{
    public RemediationException() { }
    public RemediationException(string message) : base(message) { }
    public RemediationException(string message, Exception inner) : base(message, inner) { }
}

public class NotificationException : ServiceException
{
    public NotificationException() { }
    public NotificationException(string message) : base(message) { }
    public NotificationException(string message, Exception inner) : base(message, inner) { }
}
