using OCR_BACKEND.Modals;
using System.Data;

namespace OCR_BACKEND.Services
{
    public interface IUserAddService
    {
        Task<int> InsertUpdateUserAsync(UserRequest user);
        Task<DataTable> UsersGET(PaginationRequest model);
    }

    public class UserAddService : IUserAddService
    {
        private readonly UserAddDBHelper _db;

        public UserAddService(UserAddDBHelper db)
        {
            _db = db;
        }

        public async Task<int> InsertUpdateUserAsync(UserRequest user)
        {
            if (!string.IsNullOrEmpty(user.UserPass))
            {
                user.UserPass = PasswordHelper.HashPassword(user.UserPass);
            }

            return await _db.InsertUpdateUserAsync(user);
        }
        public async Task<DataTable> UsersGET(PaginationRequest model)
        {
            return await _db.UsersGET(model);
        }
    }
}
