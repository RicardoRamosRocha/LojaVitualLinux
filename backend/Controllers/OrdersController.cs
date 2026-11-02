using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using LojaVirtual.Data;
using LojaVirtual.Models;
using LojaVirtual.Services;

namespace LojaVirtual.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class OrdersController : Controller
    {
        private readonly AppDbContext _context;

        public OrdersController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Orders
        public async Task<IActionResult> Index()
        {
            return View(await _context.Orders
                .Include(order => order.PaymentAttempts)
                .AsNoTracking()
                .OrderByDescending(order => order.CreatedAt)
                .ToListAsync());
        }

        // GET: Orders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var order = await _context.Orders
                .Include(order => order.Items)
                .Include(order => order.PaymentAttempts)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // GET: Orders/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Orders/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,CustomerName,Email,Phone,Address,Total,CreatedAt")] Order order)
        {
            if (ModelState.IsValid)
            {
                order.Status = OrderStatusWorkflow.Pending;
                _context.Add(order);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string nextStatus)
        {
            var order = await _context.Orders
                .Include(item => item.PaymentAttempts)
                .FirstOrDefaultAsync(item => item.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            if (!OrderStatusWorkflow.IsAllowedTransition(order.Status, nextStatus))
            {
                TempData["StatusError"] = "A transição de status solicitada não é permitida.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var paymentStatus = order.PaymentAttempts
                .OrderByDescending(item => item.AttemptNumber)
                .Select(item => item.Status)
                .FirstOrDefault();
            if (order.PaymentAttempts.Count > 0 && paymentStatus != PaymentStatusWorkflow.Approved)
            {
                TempData["StatusError"] = "O pedido só pode avançar após pagamento aprovado e estoque confirmado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            order.Status = nextStatus;
            await _context.SaveChangesAsync();

            TempData["StatusSuccess"] = $"Status atualizado para {nextStatus}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Orders/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                return NotFound();
            }
            return View(order);
        }

        // POST: Orders/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,CustomerName,Email,Phone,Address")] Order order)
        {
            if (id != order.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingOrder = await _context.Orders.FirstOrDefaultAsync(item => item.Id == id);
                    if (existingOrder == null)
                    {
                        return NotFound();
                    }

                    existingOrder.CustomerName = order.CustomerName;
                    existingOrder.Email = order.Email;
                    existingOrder.Phone = order.Phone;
                    existingOrder.Address = order.Address;
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!OrderExists(order.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(order);
        }

        // GET: Orders/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var order = await _context.Orders
                .FirstOrDefaultAsync(m => m.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // POST: Orders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                _context.Orders.Remove(order);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool OrderExists(int id)
        {
            return _context.Orders.Any(e => e.Id == id);
        }
    }
}
