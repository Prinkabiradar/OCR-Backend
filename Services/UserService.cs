using OCR_BACKEND.Modals; 

namespace OCR_BACKEND.Services
{
    public interface IUserService
    {
        Task<User?> AuthenticateUserAsync(string username, string password);
    }

    public class UserService : IUserService
    {
        private readonly UserDBHelper _db;

        public UserService(UserDBHelper db)
        {
            _db = db;
        }

        public Task<User?> AuthenticateUserAsync(string username, string password)
        {
            return _db.AuthenticateUserAsync(username, password);
        }
    }

}
