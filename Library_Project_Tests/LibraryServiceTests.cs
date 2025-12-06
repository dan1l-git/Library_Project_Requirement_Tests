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

    }
}
