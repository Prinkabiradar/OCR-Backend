using OCR_BACKEND.Modals;

namespace OCR_BACKEND.Services
{
    public interface IUserAddService
    {
        Task<int> InsertUpdateUserAsync(UserRequest user);
    }

    public class UserAddService : IUserAddService
    {
        private readonly UserAddDBHelper _db;

        public UserAddService(UserAddDBHelper db)
        {
            _db = db;
        }

        public Task<int> InsertUpdateUserAsync(UserRequest user)
        {
            return _db.InsertUpdateUserAsync(user);
        }
    }
}
