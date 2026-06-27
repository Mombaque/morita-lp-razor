function getImage(carouselId, isNext) {
    const imgs = document.querySelectorAll(`#${carouselId} .carousel-img`);
    if (imgs.length === 0) return;
    let current = [...imgs].findIndex(img => img.classList.contains("active"));
    if (current === -1) current = 0; 

    imgs[current].classList.remove("active");
    let index = isNext
        ? (current + 1) % imgs.length
        : (current - 1 + imgs.length) % imgs.length;
    imgs[index].classList.add("active");
}

window.getImage = getImage;
