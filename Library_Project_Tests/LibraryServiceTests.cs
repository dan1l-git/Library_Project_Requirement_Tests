using Library_Project.Model;
using Library_Project.Models;
using Library_Project.Services;
using Library_Project.Services.Interfaces;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Library_Project.Services.Interfaces;
namespace Library_Project_Tests
{
    public class LibraryServiceTests
    {
        private readonly Mock<IBookRepository> _bookRepo;
        private readonly Mock<IMemberService> _member;
        private readonly Mock<INotificationService> _notification;
        private readonly Mock<IAuditService> _audit;
        private readonly Mock<IBorrowRepository> _borrowRepo;
        private readonly Mock<IFineService> _fineService;

        private LibraryService CreateService()
        {
            return new LibraryService(
                _bookRepo.Object,
                _member.Object,
                _notification.Object,
                _audit.Object,
                _borrowRepo.Object,
                _fineService.Object
            );
        }
        
        private readonly LibraryService _service;
        
        public LibraryServiceTests()
        {
            _bookRepo = new Mock<IBookRepository>();
            _member = new Mock<IMemberService>();
            _notification = new Mock<INotificationService>();
            _audit = new Mock<IAuditService>();
            _borrowRepo = new Mock<IBorrowRepository>();
            _fineService = new Mock<IFineService>();

            _service = new LibraryService(
                _bookRepo.Object,
                _member.Object,
                _notification.Object,
                _audit.Object,
                _borrowRepo.Object,
                _fineService.Object
            );
        }
        
        // R1. Назва повинна складатися щонайменше з 3 символів.
        [Fact]
        public void Requirement01_AddBook_ShouldThrowException_WhenTitleIsTooShort()
        {
            // Arrange
            string shortTitle = "Ab";

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => _service.AddBook(shortTitle, 10));
            Assert.Contains("at least 3 characters", ex.Message);
        }
        
        // R2. Максимальна кількість примірників однієї книги — 100.
        [Fact]
        public void Requirement02_AddBook_ShouldThrowException_WhenCopiesExceed100()
        {
            // Arrange
            int invalidCopies = 101;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => _service.AddBook("Valid Title", invalidCopies));
            Assert.Contains("between 1 and 100", ex.Message);
        }
        
        // R3. Бібліотека може зберігати не більше 500 книг.
        [Fact]
        public void Requirement03_AddBook_ShouldThrowException_WhenLibraryIsFull()
        {
            // Arrange
            // Імітуємо, що в бібліотеці вже є 500 книг (наприклад, 5 книг по 100 копій)
            var existingBooks = new List<Book>
            {
                new Book { Title = "B1", Copies = 100 },
                new Book { Title = "B2", Copies = 100 },
                new Book { Title = "B3", Copies = 100 },
                new Book { Title = "B4", Copies = 100 },
                new Book { Title = "B5", Copies = 100 }
            };
            _bookRepo.Setup(repo => repo.GetAllBooks()).Returns(existingBooks);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _service.AddBook("New Book", 1));
            Assert.Equal("Library capacity exceeded.", ex.Message);
        }
        
        // R4. Деякі книги можуть бути позначені як довідкові і не підлягають видачі.
        [Fact]
        public void Requirement04_BorrowBook_ShouldThrowException_WhenBookIsReferenceOnly()
        {
            // Arrange
            int memberId = 1;
            string title = "Dictionary";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 5, IsReferenceOnly = true };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(memberId, title));
            Assert.Equal("Reference books cannot be borrowed.", ex.Message);
        }
        
        // R5. Книги мають термін повернення: термін видачі = 14 днів.
        [Fact]
        public void Requirement05_BorrowBook_ShouldSetDueDateTo14DaysFromNow()
        {
            // Arrange
            int memberId = 1;
            string title = "Novel";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 5 };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act
            _service.BorrowBook(memberId, title);

            // Assert
            // Перевіряємо, що метод SaveBorrow викликався з об'єктом, у якого DueDate = Today + 14
            _borrowRepo.Verify(repo => repo.SaveBorrow(It.Is<BorrowRecord>(b => 
                b.DueDate.Date == DateTime.Now.AddDays(14).Date
            )), Times.Once);
        }
        
        // R6. При поверненні після терміну система встановлює IsLate = true.
        [Fact]
        public void Requirement06_ReturnBook_ShouldSetIsLateTrue_WhenReturnedAfterDueDate()
        {
            // Arrange
            int memberId = 1;
            string title = "Old Book";
            // Імітуємо запис, де термін сплив вчора
            var borrowRecord = new BorrowRecord 
            { 
                MemberId = memberId, 
                Title = title, 
                DueDate = DateTime.Now.AddDays(-1),
                IsLate = false 
            };

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title)).Returns(borrowRecord);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });
            
            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());
            // Act
            _service.ReturnBook(memberId, title, signatureConfirmed: true);

            // Assert
            Assert.True(borrowRecord.IsLate);
            _borrowRepo.Verify(r => r.UpdateBorrow(borrowRecord), Times.Once);
        }
        
        // R7. Член може одночасно позичити максимум 5 книг.
        [Fact]
        public void Requirement07_BorrowBook_ShouldThrow_WhenLimitExceeded()
        {
            // Arrange
            int memberId = 1;
            string title = "Book 6";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 5 };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            
            // Імітуємо, що у користувача вже є 5 активних книг
            var activeBorrows = new List<BorrowRecord> 
            { 
                new BorrowRecord(), new BorrowRecord(), new BorrowRecord(), new BorrowRecord(), new BorrowRecord() 
            };
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(activeBorrows);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(memberId, title));
            Assert.Equal("Borrow limit exceeded.", ex.Message);
        }
        
        // R8. Член не може позичати книги, якщо має прострочені книги.
        [Fact]
        public void Requirement08_BorrowBook_ShouldThrow_WhenMemberHasOverdueBooks()
        {
            // Arrange
            int memberId = 1;
            string title = "New Book";
            var member = new Member { Id = memberId, Status = "Active", HasOverdueBooks = true };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(memberId, title));
            Assert.Equal("Member has overdue books.", ex.Message);
        }
        
        // R9. Статус члена може бути активним або призупиненим.
        [Fact]
        public void Requirement09_ShouldPreventBorrowing_WhenMemberIsSuspended()
        {
            // Arrange
            var member = new Member {Status = "Suspended"};
            var book = new Book{ Title = "Book", Copies = 1};

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(member.Id, book.Title));
        }
        
        // R10. Член повинен підтвердити повернення цифровим підписом.
        [Fact]
        public void Requirement10_ShouldFailReturn_WhenSignatureIsMissing()
        {
            // Arrange
            int memberId = 1;
            string title = "Book Title";

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => 
                _service.ReturnBook(memberId, title, signatureConfirmed: false));

            Assert.Equal("Return must be confirmed with signature.", ex.Message);
        }
        
        // R11. Позичання створює запис BorrowRecord, який зберігається в сховищі.
        [Fact]
        public void Requirement11_BorrowBook_ShouldSaveBorrowRecord()
        {
            // Arrange
            int memberId = 1;
            string title = "Book A";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 1 };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act
            _service.BorrowBook(memberId, title);

            // Assert
            _borrowRepo.Verify(r => r.SaveBorrow(It.IsAny<BorrowRecord>()), Times.Once);
        }
        
        // R12. Система запобігає одночасному позичанню одного і того ж видання двічі.
        [Fact]
        public void Requirement12_BorrowBook_ShouldThrow_WhenBorrowingSameBookTwice()
        {
            // Arrange
            int memberId = 1;
            string title = "Book A";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 5 };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            
            // Імітуємо, що ця книга вже є у списку активних
            var activeBorrows = new List<BorrowRecord> { new BorrowRecord { Title = title } };
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(activeBorrows);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _service.BorrowBook(memberId, title));
            Assert.Equal("Cannot borrow the same book twice.", ex.Message);
        }
        
        // R13. Система реєструє всі операції позичання в службі аудиту.
        [Fact]
        public void Requirement13_BorrowBook_ShouldLogAudit()
        {
            // Arrange
            int memberId = 1;
            string title = "Book A";
            var member = new Member { Id = memberId, Status = "Active" };
            var book = new Book { Title = title, Copies = 1 };

            _member.Setup(m => m.GetMember(memberId)).Returns(member);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(book);
            _borrowRepo.Setup(br => br.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act
            _service.BorrowBook(memberId, title);

            // Assert
            _audit.Verify(a => a.LogBorrow(memberId, title), Times.Once);
        }
        
        // R14. Коли член повертає останню книгу, система надсилає повідомлення.
        [Fact]
        public void Requirement14_ReturnBook_ShouldNotifyAllReturned_WhenNoBorrowsLeft()
        {
            // Arrange
            int memberId = 1;
            string title = "Last Book";
            var borrowRecord = new BorrowRecord { MemberId = memberId, Title = title };

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title)).Returns(borrowRecord);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });
            
            // Головне: імітуємо, що після повернення активних записів немає (порожній список)
            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act
            _service.ReturnBook(memberId, title, signatureConfirmed: true);

            // Assert
            _notification.Verify(n => n.NotifyAllReturned(memberId), Times.Once);
        }
        
        // R15. Повернення книги спричиняє розрахунок штрафу, якщо вона повернута з запізненням.
        [Fact]
        public void Requirement15_ReturnBook_ShouldApplyFine_WhenLate()
        {
            // Arrange
            int memberId = 1;
            string title = "Late Book";
            // Встановлюємо дату повернення в минуле
            var borrowRecord = new BorrowRecord 
            { 
                MemberId = memberId, 
                Title = title, 
                DueDate = DateTime.Now.AddDays(-5) 
            };

            _borrowRepo.Setup(r => r.GetBorrowRecord(memberId, title)).Returns(borrowRecord);
            _bookRepo.Setup(b => b.FindBook(title)).Returns(new Book { Title = title, Copies = 1 });
            
            _borrowRepo.Setup(r => r.GetActiveBorrows(memberId)).Returns(new List<BorrowRecord>());

            // Act
            _service.ReturnBook(memberId, title, signatureConfirmed: true);

            // Assert
            _fineService.Verify(f => f.ApplyFine(memberId, title), Times.Once);
        }

    }
}
