namespace MusicBot.Exceptions;

public class InvalidInputException(string message) : Exception(message)
{
}