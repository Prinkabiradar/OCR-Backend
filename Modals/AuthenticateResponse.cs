namespace OCR_BACKEND.Modals
{
    public class AuthenticateResponse
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }

        public string authToken { get; set; } 

        public int roleId { get; set; }

        public bool error { get; set; }
        public AuthenticateResponse(User user, string token)
        {
            Id = user.UserId;
            Username = user.UserName; 
            authToken = token; 
            error = false;
            roleId = user.RoleId;

        }
    }
}
