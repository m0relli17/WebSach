using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using WebBanSach.Models.Data;
using WebBanSach.Models.Process;
using WebBanSach.Models;
using System.Web.Script.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Project8.Models.Data;
using Project8.Models.Process;

namespace WebBanSach.Controllers
{
    public class CartController : Controller
    {
        //khởi tạo dữ liệu
        BSDBContext db = new BSDBContext();

        //tạo 1 chuỗi hằng để gán session
        private const string CartSession = "CartSession";

        // GET: Cart/ : trang giỏ hàng
        public ActionResult Index()
        {
            var cart = Session[CartSession];
            var list = new List<CartModel>();
            var sl = 0;
            decimal? total = 0;
            if (cart != null)
            {
                list = (List<CartModel>)cart;
                sl = list.Sum(x => x.Quantity);
                total = list.Sum(x => x.Total);
            }
            ViewBag.Quantity = sl;
            ViewBag.Total = total;
            return View(list);
        }

        //GET : /Cart/CartHeader : đếm sổ sản phẩm trong giỏ hàng
        //PartialView : CartHeader
        public ActionResult CartHeader()
        {
            var cart = Session[CartSession];
            var list = new List<CartModel>();
            if (cart != null)
            {
                list = (List<CartModel>)cart;
            }

            return PartialView(list);
        }

        //Xóa 1 sản phẩm trong giỏ hàng
        public JsonResult Delete(int id)
        {
            var sessionCart = (List<CartModel>)Session[CartSession];
            //xóa những giá trị mà có mã sách giống với id
            if(sessionCart==null) return Json(new {status=false});
            sessionCart.RemoveAll(x => x.sach.MaSach == id);
            //gán lại giá trị cho session
            Session[CartSession] = sessionCart;

            return Json(new
            {
                status = true
            });
        }

        //Xóa tất cả các sản phẩm trong giỏ hàng
        public JsonResult DeleteAll()
        {
            Session[CartSession] = null;
            return Json(new
            {
                status = true
            });
        }

        //Cập nhật giỏ hàng
        public JsonResult Update(string cartModel)
        {
            //tạo 1 đối tượng dạng json
            var jsonCart = new JavaScriptSerializer().Deserialize<List<CartModel>>(cartModel);

            //ép kiểu từ session
            var sessionCart = (List<CartModel>)Session[CartSession];

            if(sessionCart==null || jsonCart==null || jsonCart.Any(x=>x.Quantity<1 || x.Quantity>999)) return Json(new {status=false});
            foreach (var item in sessionCart)
            {
                var jsonItem = jsonCart.FirstOrDefault(x => x.sach!=null && x.sach.MaSach == item.sach.MaSach);
                if (jsonItem != null)
                {
                    item.Quantity = jsonItem.Quantity;
                }
            }
            //cập nhật lại session
            Session[CartSession] = sessionCart;

            return Json(new
            {
                status = true
            });
        }

        //GET : /Cart/AddItem/?id=?&quantity=1 : thêm sản phẩm vào giỏ hàng
        public ActionResult AddItem(int id, int quantity)
        {
            if(quantity<1 || quantity>999) return new HttpStatusCodeResult(400);
            //lấy mã sách và gán đối tượng
            var sach = new AdminProcess().GetIdBook(id);
            if(sach==null) return HttpNotFound();

            //lấy giỏ hàng từ session
            var cart = Session[CartSession];

            //nếu đã có sản phẩm trong giỏ hàng
            if (cart != null)
            {
                var list = (List<CartModel>)cart;
                if (list.Exists(x => x.sach.MaSach == id))
                {

                    foreach (var item in list)
                    {
                        if (item.sach.MaSach == id)
                        {
                            item.Quantity = Math.Min(999,item.Quantity + quantity);
                        }
                    }
                }
                else
                {
                    //tạo mới đối tượng cart item
                    var item = new CartModel();
                    item.sach = sach;
                    item.Quantity = quantity;
                    list.Add(item);
                }

                //Gán vào session
                Session[CartSession] = list;
            }
            else
            {
                //tạo mới giỏ hàng
                var item = new CartModel();
                item.sach = sach;
                item.Quantity = quantity;
                var list = new List<CartModel>();
                list.Add(item);

                //gán vào session
                Session[CartSession] = list;
            }

            return RedirectToAction("Index");
        }

        private KhachHang CurrentCustomer() {
            string username=Session["User"] as string;
            if(string.IsNullOrEmpty(username)) return null;
            return db.KhachHangs.FirstOrDefault(x=>x.TaiKhoan==username && x.TrangThai && x.EmailVerified);
        }
        [ChildActionOnly]
        public PartialViewResult UserInfo() { return PartialView(CurrentCustomer()); }
        [HttpGet]
        public ActionResult Payment() {
            if(CurrentCustomer()==null) return RedirectToAction("LoginPage","User");
            var cart=Session[CartSession] as List<CartModel> ?? new List<CartModel>();
            foreach(var item in cart) {
                var book=db.Saches.Find(item.sach.MaSach);
                if(book!=null) item.sach=book;
            }
            ViewBag.Quantity=cart.Sum(x=>x.Quantity); ViewBag.Total=cart.Sum(x=>x.Total);
            return View(cart);
        }
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Payment(FormCollection f) {
            var kh=CurrentCustomer();
            if(kh==null) return RedirectToAction("LoginPage","User");
            var cart=Session[CartSession] as List<CartModel>;
            if(cart==null || cart.Count==0) return RedirectToAction("Index");
            if(f["PaymentMethod"]!="1") { TempData["CartMessage"]="Hiện chỉ hỗ trợ thanh toán khi nhận hàng (COD)."; return RedirectToAction("Payment"); }
            // Không nhận giá, tổng tiền hoặc mã khách hàng từ trình duyệt.
            using(var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable)) {
                try {
                    var order=new DonDatHang {MaKH=kh.MaKH,NgayDat=DateTime.Now,NgayGiao=DateTime.Now.AddDays(3),TinhTrang=false,Tracking=1,ThanhToan=0};
                    db.DonDatHangs.Add(order); db.SaveChanges();
                    foreach(var line in cart.GroupBy(x=>x.sach.MaSach)) {
                        var book=db.Saches.Find(line.Key);
                        long qty=line.Sum(x=>(long)x.Quantity);
                        if(line.Any(x=>x.Quantity<1) || qty>999 || book==null || !book.GiaBan.HasValue || book.GiaBan<0 || book.SoLuongTon.GetValueOrDefault()<qty)
                            throw new InvalidOperationException("Sách trong giỏ đã hết hàng hoặc số lượng không hợp lệ. Vui lòng cập nhật giỏ hàng.");
                        book.SoLuongTon=book.SoLuongTon.GetValueOrDefault()-(int)qty;
                        db.ChiTietDDHs.Add(new ChiTietDDH {MaDDH=order.MaDDH,MaSach=book.MaSach,SoLuong=(int)qty,DonGia=book.GiaBan});
                    }
                    db.SaveChanges(); tx.Commit(); Session[CartSession]=null;
                    return RedirectToAction("TrackingOderDetails",new {id=order.MaDDH});
                } catch(InvalidOperationException e) { tx.Rollback(); TempData["CartMessage"]=e.Message; }
                catch(Exception e) { tx.Rollback(); log.Error("Checkout failed",e); TempData["CartMessage"]="Chưa tạo được đơn. Giỏ hàng được giữ lại; vui lòng thử lại."; }
            }
            return RedirectToAction("Payment");
        }

        public ActionResult Success()
        {
            return View();
        }

        public ActionResult TrackingOder(int? status) {
            var kh=CurrentCustomer(); if(kh==null) return RedirectToAction("LoginPage","User");
            var query=db.DonDatHangs.Where(x=>x.MaKH==kh.MaKH);
            if(status.HasValue) query=query.Where(x=>x.Tracking==status.Value);
            return View(query.OrderByDescending(x=>x.MaDDH).ToList());
        }
        public ActionResult TrackingOderDetails(int id) {
            var kh=CurrentCustomer(); if(kh==null) return RedirectToAction("LoginPage","User");
            var order=db.DonDatHangs.FirstOrDefault(x=>x.MaDDH==id && x.MaKH==kh.MaKH);
            if(order==null) return HttpNotFound();
            return View(order);
        }
        public JsonResult loadOrder() {
            var kh=CurrentCustomer(); if(kh==null) { Response.StatusCode=401; return Json(new {error="Vui lòng đăng nhập"},JsonRequestBehavior.AllowGet); }
            var data=db.DonDatHangs.Where(x=>x.MaKH==kh.MaKH).Select(x=>new {x.MaDDH,x.NgayDat,x.NgayGiao,x.Tracking,x.ThanhToan}).ToList();
            return Json(new {data=data},JsonRequestBehavior.AllowGet);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CancelOrder(int id) {
            var kh=CurrentCustomer(); if(kh==null) return RedirectToAction("LoginPage","User");
            using(var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable)) {
                var order=db.DonDatHangs.FirstOrDefault(x=>x.MaDDH==id && x.MaKH==kh.MaKH);
                if(order==null) return HttpNotFound();
                if(order.Tracking!=1) { TempData["OrderMessage"]="Chỉ có thể hủy đơn đang chờ xử lý."; return RedirectToAction("TrackingOderDetails",new{id=id}); }
                foreach(var line in order.ChiTietDDHs) { var book=db.Saches.Find(line.MaSach); if(book!=null) book.SoLuongTon=book.SoLuongTon.GetValueOrDefault()+line.SoLuong.GetValueOrDefault(); }
                order.Tracking=5; order.TinhTrang=false; db.SaveChanges(); tx.Commit();
            }
            return RedirectToAction("TrackingOderDetails",new{id=id});
        }
    }
}
