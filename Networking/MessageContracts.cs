// AetherDraw/Networking/MessageContracts.cs
namespace AetherDraw.Networking
{
    /// <summary>
    /// Defines the different types of messages that can be sent between the client and server.
    /// Each value represents a single byte that will prefix the message payload.
    /// </summary>
    public enum MessageType : byte
    {
        // Object-related messages
        ADD_OBJECTS,
        DELETE_OBJECT,
        MOVE_OBJECT,

        // Page-state messages
        CLEAR_PAGE,
        REPLACE_FULL_PAGE_STATE,

        // Session management messages (to be implemented)
        AUTHENTICATE,
        AUTHENTICATION_SUCCESS,
        AUTHENTICATION_FAILURE,
    }
}
