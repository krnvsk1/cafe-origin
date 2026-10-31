using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;
using Kumashaurma.API.Options;
using Kumashaurma.API.Services;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Extensions;

namespace Kumashaurma.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrdersController> _logger;
        private readonly RestaurantOptions _restaurant;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;
        private readonly IPaymentService _payments;
        private readonly IPermissionService _permissions;

        public OrdersController(
            ApplicationDbContext context,
            ILogger<OrdersController> logger,
            IOptions<RestaurantOptions> restaurant,
            IOrderStatusService statusService,
            IOrderEventBus events,
            IPaymentService payments,
            IPermissionService permissions)
        {
            _payments = payments;
            _permissions = permissions;
            _context = context;
            _logger = logger;
            _restaurant = restaurant.Value;
            _statusService = statusService;
            _events = events;
        }

        [HttpGet]
        [HasPermission(Permissions.OrdersView, Permissions.Kitchen, Permissions.Delivery)]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var orders = await _context.Orders
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
                    
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении заказов");
                return StatusCode(500, new { Message = "Ошибка сервера при получении заказов" });
            }
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<IActionResult> GetMyOrders()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(new { Message = "Пользователь не авторизован" });
                }

                var orders = await _context.Orders
                    .Where(o => o.UserId == userId)
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();
                    
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении заказов пользователя");
                return StatusCode(500, new { Message = "Ошибка сервера при получении заказов" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id, [FromQuery] string? token)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .FirstOrDefaultAsync(o => o.Id == id);
                    
                if (order == null)
                    return NotFound(new { Message = $"Заказ с ID {id} не найден" });

                // Персонал видит все заказы, клиент — свои, гость — по токену из ссылки
                if (!CanAccessOrder(order, token))
                    return NotFound(new { Message = $"Заказ с ID {id} не найден" });

                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении заказа ID {OrderId}", id);
                return StatusCode(500, new { Message = "Ошибка сервера при получении заказа" });
            }
        }

        /// <summary>
        /// Доступ к заказу: персонал — ко всем, клиент — к своим, гость — по секретному токену
        /// </summary>
        private bool CanAccessOrder(Order order, string? token)
        {
            if (User.IsStaff())
                return true;

            var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(currentUserIdClaim, out var currentUserId) && order.UserId == currentUserId)
                return true;

            return !string.IsNullOrEmpty(token)
                && !string.IsNullOrEmpty(order.AccessToken)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(token),
                    System.Text.Encoding.UTF8.GetBytes(order.AccessToken));
        }

        /// <summary>
        /// Данные для повтора заказа: позиции с актуальными ценами и доступностью
        /// </summary>
        [HttpGet("{id}/reorder")]
        [Authorize]
        public async Task<IActionResult> GetReorder(int id)
        {
            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.SelectedAddons)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound(new { Message = $"Заказ с ID {id} не найден" });

            var isStaff = User.IsStaff();
            var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!isStaff && (!int.TryParse(currentUserIdClaim, out var currentUserId) || order.UserId != currentUserId))
                return NotFound(new { Message = $"Заказ с ID {id} не найден" });

            var shawarmaIds = order.OrderItems.Select(i => i.ShawarmaId).Distinct().ToList();
            var shawarmas = await _context.Shawarmas
                .AsNoTracking()
                .Include(s => s.Parent)
                .Where(s => shawarmaIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            var addonIds = order.OrderItems
                .SelectMany(i => i.SelectedAddons)
                .Select(a => a.AddonId)
                .Distinct()
                .ToList();
            var addons = await _context.Addons
                .AsNoTracking()
                .Include(a => a.Category)
                .Where(a => addonIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            var items = order.OrderItems.Select(item =>
            {
                shawarmas.TryGetValue(item.ShawarmaId, out var current);
                var parentAvailable = current?.Parent == null || current.Parent.IsAvailable;
                return new
                {
                    item.ShawarmaId,
                    CardId = current?.ParentId ?? item.ShawarmaId,
                    Name = current != null
                        ? (current.Parent != null ? $"{current.Parent.Name} — {current.Name}" : current.Name)
                        : item.Name,
                    item.Quantity,
                    Available = current != null && current.IsAvailable && parentAvailable,
                    Price = current?.Price ?? item.Price,
                    OldPrice = item.Price,
                    Addons = item.SelectedAddons.Select(a =>
                    {
                        addons.TryGetValue(a.AddonId, out var addon);
                        return new
                        {
                            a.AddonId,
                            Name = addon?.Name ?? a.AddonName,
                            Price = addon?.Price ?? a.Price,
                            CategoryId = addon?.AddonCategoryId ?? a.AddonCategoryId,
                            CategoryName = addon?.Category?.Name ?? a.AddonCategoryName,
                            a.Quantity,
                            Available = addon != null && addon.IsAvailable
                        };
                    }).ToList()
                };
            }).ToList();

            return Ok(new { OrderId = order.Id, Items = items });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
        {
            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.CustomerName))
                    return BadRequest(new { Message = "Имя клиента обязательно" });
                    
                if (request.Items == null || !request.Items.Any())
                    return BadRequest(new { Message = "Добавьте хотя бы один товар в заказ" });

                // Валидация количества
                foreach (var itemRequest in request.Items)
                {
                    if (itemRequest.Quantity <= 0)
                        return BadRequest(new { Message = $"Количество товара '{itemRequest.Name}' должно быть больше 0" });

                    if (itemRequest.SelectedAddons != null)
                    {
                        foreach (var selectedAddon in itemRequest.SelectedAddons)
                        {
                            if (selectedAddon.Quantity <= 0)
                                return BadRequest(new { Message = "Количество добавки должно быть больше 0" });
                        }
                    }
                }

                // Способ оплаты
                var paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "cash" : request.PaymentMethod.Trim().ToLowerInvariant();
                if (paymentMethod is not ("cash" or "card" or "online"))
                    return BadRequest(new { Message = "Неизвестный способ оплаты" });
                if (paymentMethod == "online" && !_payments.OnlineEnabled)
                    return BadRequest(new { Message = "Онлайн-оплата сейчас недоступна" });

                // Заказ ко времени
                DateTime? scheduledFor = null;
                if (request.ScheduledFor.HasValue)
                {
                    var requested = request.ScheduledFor.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(request.ScheduledFor.Value, DateTimeKind.Utc)
                        : request.ScheduledFor.Value.ToUniversalTime();
                    var nowUtc = DateTime.UtcNow;

                    if (requested < nowUtc.AddMinutes(_restaurant.MinLeadMinutes))
                        return BadRequest(new { Message = $"Заказ ко времени можно оформить минимум за {_restaurant.MinLeadMinutes} минут" });
                    if (requested > nowUtc.AddDays(_restaurant.MaxDaysAhead))
                        return BadRequest(new { Message = $"Заказ можно оформить не более чем на {_restaurant.MaxDaysAhead} дней вперёд" });
                    if (!_restaurant.IsWithinWorkingHours(requested))
                        return BadRequest(new { Message = $"Выберите время в часы работы: {_restaurant.OpenTime}–{_restaurant.CloseTime}" });

                    scheduledFor = requested;
                }

                // Получаем актуальные цены из базы данных (только доступные шаурмы)
                var shawarmaIds = request.Items.Select(i => i.ShawarmaId).Distinct();
                var shawarmas = await _context.Shawarmas
                    .Where(s => s.IsAvailable && shawarmaIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Price);

                // Проверяем, что все запрошенные шаурмы существуют и доступны
                var missingShawarmaIds = shawarmaIds.Where(id => !shawarmas.ContainsKey(id)).ToList();
                if (missingShawarmaIds.Any())
                    return BadRequest(new { Message = $"Шаурма с ID {string.Join(", ", missingShawarmaIds)} не найдена или недоступна" });

                // Получаем все добавки для проверки цен
                var allAddonIds = request.Items
                    .Where(i => i.SelectedAddons != null)
                    .SelectMany(i => i.SelectedAddons!)
                    .Select(a => a.AddonId)
                    .Distinct()
                    .ToList();

                var addons = new Dictionary<int, Addon>();
                if (allAddonIds.Any())
                {
                    addons = await _context.Addons
                        .Include(a => a.Category)
                        .Where(a => allAddonIds.Contains(a.Id) && a.IsAvailable)
                        .ToDictionaryAsync(a => a.Id, a => a);

                    // Добавки из стоп-листа заказать нельзя
                    var unavailableAddonIds = allAddonIds.Where(id => !addons.ContainsKey(id)).ToList();
                    if (unavailableAddonIds.Any())
                        return BadRequest(new { Message = "Некоторые добавки сейчас недоступны. Обновите корзину." });
                }

                // Получаем UserId если пользователь авторизован
                int? userId = null;
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedUserId))
                {
                    userId = parsedUserId;
                }

                // Валидация промокода
                decimal discountAmount = 0;
                PromoCode? promoCode = null;
                if (request.PromoCodeId.HasValue)
                {
                    promoCode = await _context.PromoCodes.FindAsync(request.PromoCodeId.Value);
                    if (promoCode == null || !promoCode.IsActive)
                        return BadRequest(new { Message = "Промокод не найден или неактивен" });
                }

                // Зона доставки: если зоны настроены, адрес доставки должен в них попадать
                DeliveryZone? deliveryZone = null;
                var deliveryType = !string.IsNullOrEmpty(request.DeliveryType) ? request.DeliveryType : "Доставка";

                // Позиции с ограничением по способу получения (например, пиво — только в зале)
                var restricted = await _context.Shawarmas.AsNoTracking()
                    .Where(s => shawarmaIds.Contains(s.Id) && (s.OrderTypes != null || (s.Parent != null && s.Parent.OrderTypes != null)))
                    .Select(s => new { s.Name, s.OrderTypes, ParentName = s.Parent != null ? s.Parent.Name : null, ParentTypes = s.Parent != null ? s.Parent.OrderTypes : null })
                    .ToListAsync();
                var blocked = restricted.FirstOrDefault(r => !SalesChannels.Allows(r.OrderTypes, r.ParentTypes, deliveryType));
                if (blocked != null)
                {
                    var allowed = SalesChannels.Parse(blocked.OrderTypes) ?? SalesChannels.Parse(blocked.ParentTypes) ?? SalesChannels.All;
                    return BadRequest(new { Message = $"«{blocked.ParentName ?? blocked.Name}» можно заказать {SalesChannels.Describe(allowed)} — уберите позицию из корзины или выберите другой способ получения" });
                }
                if (deliveryType == "Доставка")
                {
                    var zones = await _context.DeliveryZones.AsNoTracking()
                        .Where(z => z.IsActive)
                        .OrderBy(z => z.SortOrder).ThenBy(z => z.Id)
                        .ToListAsync();
                    if (zones.Count > 0)
                    {
                        if (request.Latitude is not double lat || request.Longitude is not double lng)
                            return BadRequest(new { Message = "Укажите адрес доставки на карте" });
                        deliveryZone = zones.FirstOrDefault(z => z.Contains(lat, lng));
                        if (deliveryZone == null)
                            return BadRequest(new { Message = "К сожалению, по этому адресу мы пока не доставляем" });
                    }
                }

                // Скидка баллами: только для авторизованных и в пределах баланса
                var requestedPoints = (int)Math.Floor(request.PointsDiscountAmount ?? 0);
                if (requestedPoints < 0)
                    return BadRequest(new { Message = "Некорректное количество баллов" });
                if (requestedPoints > 0 && userId == null)
                    return BadRequest(new { Message = "Списание баллов доступно только после входа" });

                // Весь заказ сохраняется атомарно: при любой ошибке ничего не запишется
                await using var dbTransaction = await _context.Database.BeginTransactionAsync();

                // Создаем заказ
                var newOrder = new Order
                {
                    UserId = userId,
                    CustomerName = request.CustomerName.Trim(),
                    Phone = request.Phone?.Trim() ?? string.Empty,
                    Address = request.Address?.Trim() ?? string.Empty,
                    Total = 0,
                    Status = "Новый",
                    Notes = request.Notes,
                    DeliveryType = deliveryType,
                    DeliveryZoneId = deliveryZone?.Id,
                    Latitude = deliveryType == "Доставка" ? request.Latitude : null,
                    Longitude = deliveryType == "Доставка" ? request.Longitude : null,
                    PromoCodeId = request.PromoCodeId,
                    DiscountAmount = 0,
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = null,
                    ScheduledFor = scheduledFor,
                    PaymentMethod = paymentMethod,
                    PaymentStatus = "unpaid",
                    AccessToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
                };

                await _context.Orders.AddAsync(newOrder);
                await _context.SaveChangesAsync();

                decimal total = 0;
                
                // Добавляем позиции заказа
                foreach (var itemRequest in request.Items)
                {
                    var basePrice = shawarmas.GetValueOrDefault(itemRequest.ShawarmaId, 0);
                    
                    var orderItem = new OrderItem
                    {
                        OrderId = newOrder.Id,
                        ShawarmaId = itemRequest.ShawarmaId,
                        Name = itemRequest.Name?.Trim() ?? "Без названия",
                        Quantity = itemRequest.Quantity,
                        Price = basePrice
                    };
                    
                    await _context.OrderItems.AddAsync(orderItem);
                    await _context.SaveChangesAsync(); // Сохраняем, чтобы получить Id

                    decimal addonsTotal = 0;
                    
                    // Добавляем выбранные добавки
                    if (itemRequest.SelectedAddons != null && itemRequest.SelectedAddons.Any())
                    {
                        foreach (var selectedAddon in itemRequest.SelectedAddons)
                        {
                            if (addons.TryGetValue(selectedAddon.AddonId, out var addon))
                            {
                                var addonPrice = addon.Price * selectedAddon.Quantity;
                                
                                var orderItemAddon = new OrderItemAddon
                                {
                                    OrderItemId = orderItem.Id,
                                    AddonId = addon.Id,
                                    AddonName = addon.Name,
                                    AddonCategoryId = addon.AddonCategoryId,
                                    AddonCategoryName = addon.Category?.Name ?? "Добавки",
                                    Price = addon.Price,
                                    Quantity = selectedAddon.Quantity
                                };
                                
                                await _context.OrderItemAddons.AddAsync(orderItemAddon);
                                addonsTotal += addonPrice;
                            }
                        }
                    }
                    
                    await _context.SaveChangesAsync(); // Сохраняем добавки
                    
                    total += (basePrice + addonsTotal) * itemRequest.Quantity;
                }

                // Минимальная сумма и стоимость доставки по зоне
                decimal deliveryPrice = 0;
                if (deliveryZone != null)
                {
                    if (total < deliveryZone.MinOrderAmount)
                        return BadRequest(new { Message = $"Минимальная сумма заказа для зоны «{deliveryZone.Name}» — {deliveryZone.MinOrderAmount:0.##} ₽" });
                    deliveryPrice = deliveryZone.PriceFor(total);
                }
                newOrder.DeliveryPrice = deliveryPrice;

                // Применяем промокод
                if (promoCode != null)
                {
                    discountAmount = promoCode.CalculateDiscount(total);
                    if (discountAmount > 0)
                    {
                        promoCode.CurrentUses++;
                        _context.PromoCodes.Update(promoCode);
                    }
                }

                newOrder.DiscountAmount = discountAmount;

                // Списываем баллы на сервере (1 балл = 1 рубль), не больше баланса и суммы заказа
                int pointsDiscount = 0;
                if (requestedPoints > 0)
                {
                    var user = await _context.Users.FindAsync(userId!.Value);
                    if (user == null)
                        return BadRequest(new { Message = "Пользователь не найден" });

                    var maxByTotal = (int)Math.Floor(Math.Max(0, total - discountAmount));
                    if (requestedPoints > user.PointsBalance)
                        return BadRequest(new { Message = $"Недостаточно баллов. Текущий баланс: {user.PointsBalance}" });
                    if (requestedPoints > maxByTotal)
                        return BadRequest(new { Message = $"Баллами можно оплатить не более {maxByTotal} ₽" });

                    pointsDiscount = requestedPoints;

                    // Атомарное списание: защищает от двойной траты при параллельных заказах
                    var updated = await _context.Users
                        .Where(u => u.Id == user.Id && u.PointsBalance >= pointsDiscount)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.PointsBalance, u => u.PointsBalance - pointsDiscount));
                    if (updated == 0)
                        return BadRequest(new { Message = "Недостаточно баллов" });

                    _context.UserPointsTransactions.Add(new UserPointsTransaction
                    {
                        UserId = user.Id,
                        Type = "spent",
                        Amount = -pointsDiscount,
                        Description = $"Оплата баллами заказа #{newOrder.Id}",
                        OrderId = newOrder.Id,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Обновляем общую сумму заказа (с учётом промокода и баллов)
                newOrder.Total = Math.Max(0, total - discountAmount - pointsDiscount) + deliveryPrice;
                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                _logger.LogInformation("✅ Заказ создан с ID: {OrderId}, клиент: {CustomerName}, итого: {Total} (скидка промо: {PromoDiscount}, баллы: {PointsDiscount})", 
                    newOrder.Id, newOrder.CustomerName, newOrder.Total, discountAmount, pointsDiscount);

                // Загружаем полный заказ с позициями для ответа
                var createdOrder = await _context.Orders
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .FirstOrDefaultAsync(o => o.Id == newOrder.Id);

                // Рассчитываем баллы, которые будут начислены за заказ (1 балл за 100 рублей)
                var pointsEarned = (int)(Math.Floor(newOrder.Total / 100));

                // Онлайн-заказ уходит на кухню только после оплаты (событие публикует PaymentService)
                string? confirmationUrl = null;
                if (paymentMethod == "online")
                {
                    confirmationUrl = await _payments.StartAsync(newOrder);
                    if (confirmationUrl == null)
                        _logger.LogWarning("Не удалось создать платёж для заказа #{OrderId}", newOrder.Id);
                }
                else
                {
                    _events.Publish(new OrderEvent("created", newOrder.Id, newOrder.UserId, newOrder.Status, null, DateTime.UtcNow));
                }

                return CreatedAtAction(nameof(GetById), new { id = newOrder.Id }, new
                {
                    Order = createdOrder,
                    PointsEarned = pointsEarned,
                    // Токен для отслеживания заказа без входа (сохраняется на устройстве клиента)
                    AccessToken = newOrder.AccessToken,
                    // Ссылка на оплату (для онлайн-оплаты); null — оплатить можно позже со страницы заказа
                    ConfirmationUrl = confirmationUrl
                });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка базы данных при создании заказа");
                return StatusCode(500, new { Message = "Ошибка при сохранении заказа в базу данных" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании заказа");
                return StatusCode(500, new { Message = "Внутренняя ошибка сервера" });
            }
        }

        // Обновленный DTO для запроса
        public class CreateOrderRequest
        {
            public string CustomerName { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string? Notes { get; set; }
            public string DeliveryType { get; set; } = "Доставка";
            public int? PromoCodeId { get; set; }
            public decimal? PointsDiscountAmount { get; set; }
            /// <summary>Координаты адреса доставки (с карты)</summary>
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            /// <summary>cash | card | online</summary>
            public string? PaymentMethod { get; set; }
            /// <summary>Желаемое время (UTC, ISO 8601). null — как можно скорее</summary>
            public DateTime? ScheduledFor { get; set; }
            public List<CreateOrderItemRequest> Items { get; set; } = new();
        }

        public class CreateOrderItemRequest
        {
            public int ShawarmaId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public List<SelectedAddonDto>? SelectedAddons { get; set; }
        }

        public class SelectedAddonDto
        {
            public int AddonId { get; set; }
            public int Quantity { get; set; } = 1;
        }

        [HttpPut("{id}")]
        [HasPermission(Permissions.OrdersManage)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateOrderRequest request)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .FirstOrDefaultAsync(o => o.Id == id);
                if (order == null)
                    return NotFound(new { Message = $"Заказ с ID {id} не найден" });

                if (request.Total > 0)
                    order.Total = request.Total;

                if (!string.IsNullOrEmpty(request.CustomerName))
                    order.CustomerName = request.CustomerName.Trim();

                if (request.Phone != null)
                    order.Phone = request.Phone.Trim();

                if (request.Address != null)
                    order.Address = request.Address.Trim();

                if (request.Notes != null)
                    order.Notes = request.Notes.Trim();

                if (!string.IsNullOrEmpty(request.Status) && request.Status != order.Status)
                {
                    // Сохраняет и остальные изменённые поля, начисляет баллы, публикует событие
                    if (!await _statusService.ChangeStatusAsync(order, request.Status))
                        return BadRequest(new { Message = $"Неизвестный статус: {request.Status}" });
                    await KitchenController.LiftMarksAsync(_context, order.Id, request.Status, User.GetUserId());
                }
                else
                {
                    await _context.SaveChangesAsync();
                    _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
                }

                _logger.LogInformation("🔄 Заказ обновлен: ID {OrderId}, статус: {Status}", id, order.Status);
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении заказа ID {OrderId}", id);
                return StatusCode(500, new { Message = "Ошибка сервера при обновлении заказа" });
            }
        }

        [HttpDelete("{id}")]
        [HasPermission(Permissions.OrdersManage)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var order = await _context.Orders.FindAsync(id);
                if (order == null)
                    return NotFound(new { Message = $"Заказ с ID {id} не найден" });

                _context.Orders.Remove(order);
                
                await _context.SaveChangesAsync();
                _events.Publish(new OrderEvent("deleted", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
                
                _logger.LogInformation("🗑️ Заказ удален: ID {OrderId}, клиент: {CustomerName}", 
                    id, order.CustomerName);
                    
                return Ok(new { Message = $"Заказ {id} успешно удален", DeletedId = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении заказа ID {OrderId}", id);
                return StatusCode(500, new { Message = "Ошибка сервера при удалении заказа" });
            }
        }

        [HttpGet("stats")]
        [HasPermission(Permissions.Dashboard, Permissions.OrdersManage)]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var totalOrders = await _context.Orders.CountAsync();
                var totalRevenue = await _context.Orders.SumAsync(o => o.Total);
                var today = DateTime.UtcNow.Date;
                var todayOrders = await _context.Orders
                    .Where(o => o.CreatedAt.Date == today)
                    .CountAsync();
                    
                return Ok(new
                {
                    TotalOrders = totalOrders,
                    TotalRevenue = totalRevenue,
                    TodayOrders = todayOrders,
                    AverageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики");
                return StatusCode(500, new { Message = "Ошибка сервера при получении статистики" });
            }
        }

        [HttpPatch("{id}/status")]
        [HasPermission(Permissions.OrdersManage, Permissions.Kitchen, Permissions.Delivery, Permissions.Pos)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderRequest request)
        {
            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderItems)
                        .ThenInclude(oi => oi.SelectedAddons)
                    .FirstOrDefaultAsync(o => o.Id == id);
                if (order == null)
                    return NotFound(new { Message = $"Заказ с ID {id} не найден" });

                if (string.IsNullOrEmpty(request.Status))
                    return BadRequest(new { Message = "Укажите статус" });

                // Отменить заказ может только тот, кто управляет заказами или кассой — не кухня и не курьер
                if (request.Status == OrderStatuses.Cancelled && order.Status != OrderStatuses.Cancelled)
                {
                    var perms = await _permissions.GetPermissionsAsync(User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value));
                    if (!perms.Contains(Permissions.OrdersManage) && !perms.Contains(Permissions.Pos))
                        return StatusCode(403, new { Message = "Отменить заказ может только администратор или кассир" });
                }

                var notifier = HttpContext.RequestServices.GetRequiredService<WaiterReadyNotifier>();
                var readyBefore = request.Status == OrderStatuses.Ready ? await notifier.SnapshotAsync(order) : null;

                if (!await _statusService.ChangeStatusAsync(order, request.Status))
                    return BadRequest(new { Message = $"Неизвестный статус: {request.Status}" });

                await KitchenController.LiftMarksAsync(_context, order.Id, request.Status, User.GetUserId());
                if (readyBefore != null) await notifier.NotifyAsync(order, readyBefore);
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статуса заказа ID {OrderId}", id);
                return StatusCode(500, new { Message = "Ошибка сервера при обновлении статуса" });
            }
        }
    }

    public class UpdateOrderRequest
    {
        public string? Status { get; set; }
        public decimal Total { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Notes { get; set; }
    }
}