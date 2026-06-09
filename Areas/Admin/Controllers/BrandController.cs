using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class BrandController : Controller
    {
        private readonly ApplicationDbContext _context;
        public BrandController(ApplicationDbContext context) => _context = context;

        public async Task<IActionResult> Index(string search)
        {
            var query = _context.Brands.AsQueryable();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(b => b.BrandName.Contains(search));
            }
            ViewBag.Search = search;
            return View(await query.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> Save(Brands model)
        {
            if (string.IsNullOrEmpty(model.BrandName)) return RedirectToAction(nameof(Index));

            if (model.BrandId == 0)
            {
                _context.Brands.Add(model);
            }
            else
            {
                var target = await _context.Brands.FindAsync(model.BrandId);
                if (target != null)
                {
                    target.BrandName = model.BrandName;
                }
            }
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}